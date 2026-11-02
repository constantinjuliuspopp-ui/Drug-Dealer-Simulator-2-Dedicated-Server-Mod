using System.Net;
using System.Net.Sockets;
using DDS2Mod.Shared;

namespace DDS2Mod.Relay;

public sealed class RelayOptions
{
    public int GamePort { get; set; } = 7777;
    public int QueryPort { get; set; } = 7778;
    public int AgentPort { get; set; } = RelayProtocol.DefaultAgentPort;
    public string Secret { get; set; } = "";
    /// <summary>Gleichzeitige Spieler über das Relay.</summary>
    public int MaxClients { get; set; } = 8;
    public string BindAddress { get; set; } = "0.0.0.0";
}

/// <summary>
/// Reicht UDP-Pakete von Mitspielern (Spiel- und Abfrage-Port) an den angemeldeten Host-Agenten weiter und zurück.
/// Kennt keine Spiellogik und braucht keine Spieldateien.
/// </summary>
public sealed class RelayServer : IDisposable
{
    static readonly TimeSpan GameIdle = TimeSpan.FromSeconds(60);
    static readonly TimeSpan QueryIdle = TimeSpan.FromSeconds(15);
    static readonly TimeSpan AgentTimeout = TimeSpan.FromSeconds(15);
    const int MaxQuerySessions = 64;

    sealed class Session
    {
        public required uint Id;
        public required byte Channel;
        public required IPEndPoint Client;
        public DateTime LastSeen;
    }

    readonly RelayOptions _o;
    readonly Action<string, string> _log;
    readonly UdpClient _game, _query, _agentSock;
    readonly object _lock = new();
    readonly Dictionary<(IPEndPoint, byte), Session> _byClient = new();
    readonly Dictionary<uint, Session> _byId = new();
    readonly Dictionary<string, DateTime> _nonces = new();
    IPEndPoint? _agent;
    DateTime _agentLastSeen;
    uint _nextId = 1;
    DateTime _lastDropLog;

    public int GamePort { get; }
    public int QueryPort { get; }
    public int AgentPort { get; }
    public bool AgentConnected { get { lock (_lock) return _agent != null; } }
    public int ClientCount { get { lock (_lock) return _byId.Values.Count(s => s.Channel == RelayProtocol.ChannelGame); } }
    public string AgentAddress { get { lock (_lock) return _agent?.ToString() ?? ""; } }

    public RelayServer(RelayOptions options, Action<string, string> log)
    {
        if (string.IsNullOrWhiteSpace(options.Secret)) throw new ArgumentException("Relay-Schlüssel fehlt");
        _o = options;
        _log = log;
        var bind = IPAddress.Parse(options.BindAddress);
        _game = Open(bind, options.GamePort);
        _query = Open(bind, options.QueryPort);
        _agentSock = Open(bind, options.AgentPort);
        GamePort = ((IPEndPoint)_game.Client.LocalEndPoint!).Port;
        QueryPort = ((IPEndPoint)_query.Client.LocalEndPoint!).Port;
        AgentPort = ((IPEndPoint)_agentSock.Client.LocalEndPoint!).Port;
    }

    static UdpClient Open(IPAddress bind, int port)
    {
        var u = new UdpClient(new IPEndPoint(bind, port));
        RelayProtocol.IgnoreConnReset(u.Client);
        return u;
    }

    public Task RunAsync(CancellationToken ct) => Task.WhenAll(
        ClientLoop(_game, RelayProtocol.ChannelGame, ct),
        ClientLoop(_query, RelayProtocol.ChannelQuery, ct),
        AgentLoop(ct),
        Maintenance(ct));

    static async Task<UdpReceiveResult?> Receive(UdpClient u, CancellationToken ct)
    {
        try { return await u.ReceiveAsync(ct); }
        catch (OperationCanceledException) { return null; }
        catch (ObjectDisposedException) { return null; }
        catch (SocketException) { return default(UdpReceiveResult); }
    }

    async Task ClientLoop(UdpClient sock, byte channel, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var r = await Receive(sock, ct);
            if (r is null) return;
            if (r.Value.Buffer is null) continue;
            IPEndPoint? agent;
            byte[]? packet = null;
            lock (_lock)
            {
                agent = _agent;
                if (agent != null)
                {
                    var s = GetOrCreate(r.Value.RemoteEndPoint, channel);
                    if (s != null)
                    {
                        s.LastSeen = DateTime.UtcNow;
                        packet = RelayProtocol.MakeData(channel, s.Id, r.Value.Buffer);
                    }
                }
            }
            if (agent is null)
            {
                if (DateTime.UtcNow - _lastDropLog > TimeSpan.FromSeconds(30))
                {
                    _lastDropLog = DateTime.UtcNow;
                    _log("WARN", $"Paket von {r.Value.RemoteEndPoint} verworfen: kein Host angemeldet.");
                }
                continue;
            }
            if (packet != null) await Send(_agentSock, packet, agent, ct);
        }
    }

    Session? GetOrCreate(IPEndPoint client, byte channel)
    {
        if (_byClient.TryGetValue((client, channel), out var s)) return s;
        var count = _byId.Values.Count(x => x.Channel == channel);
        if (channel == RelayProtocol.ChannelGame && count >= _o.MaxClients)
        {
            if (DateTime.UtcNow - _lastDropLog > TimeSpan.FromSeconds(10))
            {
                _lastDropLog = DateTime.UtcNow;
                _log("WARN", $"{client} abgewiesen: Relay voll (MaxClients={_o.MaxClients}).");
            }
            return null;
        }
        if (channel == RelayProtocol.ChannelQuery && count >= MaxQuerySessions) return null;
        s = new Session { Id = _nextId++, Channel = channel, Client = client, LastSeen = DateTime.UtcNow };
        if (_nextId == 0) _nextId = 1;
        _byClient[(client, channel)] = s;
        _byId[s.Id] = s;
        if (channel == RelayProtocol.ChannelGame) _log("INFO", $"Mitspieler {client} verbunden (Sitzung {s.Id}).");
        return s;
    }

    async Task AgentLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var r = await Receive(_agentSock, ct);
            if (r is null) return;
            var buf = r.Value.Buffer;
            if (buf is null || buf.Length == 0) continue;
            var from = r.Value.RemoteEndPoint;
            switch (buf[0])
            {
                case RelayProtocol.Hello:
                    await HandleHello(buf, from, ct);
                    break;
                case RelayProtocol.KeepAlive:
                    if (!IsAgent(from)) break;
                    await Send(_agentSock, RelayProtocol.MakeStats(ClientCount), from, ct);
                    break;
                case RelayProtocol.Data:
                    if (!IsAgent(from) || !RelayProtocol.ParseData(buf, out var channel, out var id, out var payload)) break;
                    Session? s;
                    lock (_lock) _byId.TryGetValue(id, out s);
                    if (s is null || s.Channel != channel) break;
                    await Send(channel == RelayProtocol.ChannelGame ? _game : _query, payload.ToArray(), s.Client, ct);
                    break;
            }
        }
    }

    // Nur Pakete vom angemeldeten Agenten annehmen; jedes Paket zählt als Lebenszeichen.
    bool IsAgent(IPEndPoint from)
    {
        lock (_lock)
        {
            if (_agent is null || !_agent.Equals(from)) return false;
            _agentLastSeen = DateTime.UtcNow;
            return true;
        }
    }

    async Task HandleHello(byte[] buf, IPEndPoint from, CancellationToken ct)
    {
        if (!RelayProtocol.CheckHello(buf, _o.Secret, DateTimeOffset.UtcNow, out var nonce, out var reason))
        {
            _log("WARN", $"Anmeldung von {from} abgelehnt: {reason}.");
            await Send(_agentSock, RelayProtocol.MakeDenied(reason), from, ct);
            return;
        }
        lock (_lock)
        {
            // Ein mitgeschnittenes HELLO darf den Host nicht auf eine andere Adresse umleiten.
            if (_nonces.ContainsKey(nonce))
            {
                _log("WARN", $"Anmeldung von {from} abgelehnt: wiederholtes Paket.");
                return;
            }
            _nonces[nonce] = DateTime.UtcNow;
            if (_agent is null || !_agent.Equals(from))
            {
                if (_byId.Count > 0) _log("INFO", "Neuer Host – bestehende Verbindungen werden getrennt.");
                _byId.Clear();
                _byClient.Clear();
                _log("INFO", $"Host angemeldet: {from}. Mitspieler verbinden sich auf Port {GamePort}/UDP.");
            }
            _agent = from;
            _agentLastSeen = DateTime.UtcNow;
        }
        await Send(_agentSock, RelayProtocol.MakeWelcome(GamePort, QueryPort, _o.MaxClients), from, ct);
    }

    async Task Maintenance(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
            var closes = new List<uint>();
            IPEndPoint? agent;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                foreach (var s in _byId.Values.ToList())
                {
                    if (now - s.LastSeen <= (s.Channel == RelayProtocol.ChannelGame ? GameIdle : QueryIdle)) continue;
                    _byId.Remove(s.Id);
                    _byClient.Remove((s.Client, s.Channel));
                    closes.Add(s.Id);
                    if (s.Channel == RelayProtocol.ChannelGame) _log("INFO", $"Mitspieler {s.Client} getrennt (keine Daten mehr).");
                }
                foreach (var n in _nonces.Where(n => now - n.Value > RelayProtocol.MaxClockSkew * 2).Select(n => n.Key).ToList())
                    _nonces.Remove(n);
                if (_agent != null && now - _agentLastSeen > AgentTimeout)
                {
                    _log("WARN", $"Host {_agent} meldet sich nicht mehr – abgemeldet.");
                    _agent = null;
                    _byId.Clear();
                    _byClient.Clear();
                    closes.Clear();
                }
                agent = _agent;
            }
            if (agent != null)
                foreach (var id in closes) await Send(_agentSock, RelayProtocol.MakeClose(id), agent, ct);
        }
    }

    static async Task Send(UdpClient u, byte[] data, IPEndPoint to, CancellationToken ct)
    {
        try { await u.SendAsync(data, to, ct); }
        catch (SocketException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _game.Dispose();
        _query.Dispose();
        _agentSock.Dispose();
    }
}
