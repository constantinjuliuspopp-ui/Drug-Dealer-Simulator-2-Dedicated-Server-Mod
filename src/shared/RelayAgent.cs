using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace DDS2Mod.Shared;

/// <summary>
/// Läuft beim Host: meldet sich beim Relay an (Verbindung von innen nach außen, keine Portfreigabe nötig)
/// und reicht jede Mitspieler-Sitzung über einen eigenen lokalen UDP-Socket an das Spiel bzw. den Abfrage-Port weiter.
/// Das Spiel sieht die Mitspieler als 127.0.0.1 mit verschiedenen Ports.
/// </summary>
public sealed class RelayAgent : IDisposable
{
    static readonly TimeSpan KeepAliveEvery = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RelayTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan LinkIdle = TimeSpan.FromSeconds(90);

    sealed class Link : IDisposable
    {
        public required UdpClient Socket;
        public required byte Channel;
        public required uint Session;
        public DateTime LastSeen = DateTime.UtcNow;
        public readonly CancellationTokenSource Cts = new();
        public void Dispose() { Cts.Cancel(); Socket.Dispose(); }
    }

    readonly string _host;
    readonly int _agentPort;
    readonly string _secret;
    readonly IPEndPoint _localGame, _localQuery;
    readonly ConcurrentDictionary<uint, Link> _links = new();
    readonly CancellationTokenSource _cts = new();
    UdpClient? _udp;
    IPEndPoint? _relay;
    DateTime _lastReply;
    Task? _run;

    public bool Connected { get; private set; }
    public int RelayGamePort { get; private set; }
    public int RelayQueryPort { get; private set; }
    public int Clients { get; private set; }
    public string RelayHost => _host;
    public string LastError { get; private set; } = "";

    /// <summary>Meldungen für die Oberfläche (aus einem Hintergrund-Thread).</summary>
    public event Action<string, bool>? Status;

    public RelayAgent(string relayHost, int agentPort, string secret, int localGamePort, int localQueryPort)
    {
        _host = relayHost;
        _agentPort = agentPort;
        _secret = secret;
        _localGame = new IPEndPoint(IPAddress.Loopback, localGamePort);
        _localQuery = new IPEndPoint(IPAddress.Loopback, localQueryPort);
    }

    public void Start() => _run ??= Task.Run(() => RunAsync(_cts.Token));

    async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(_host, ct);
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? throw new SocketException((int)SocketError.HostNotFound);
                _relay = new IPEndPoint(address, _agentPort);
                using var udp = _udp = new UdpClient(AddressFamily.InterNetwork);
                RelayProtocol.IgnoreConnReset(udp.Client);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                await Session(udp, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { Fail($"Relay '{_host}' nicht gefunden."); }
            Connected = false;
            CloseAllLinks();
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // Eine Anmeldung: HELLO, dann KEEPALIVE alle 2 s; ohne Antwort 10 s lang neu anmelden.
    async Task Session(UdpClient udp, CancellationToken ct)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _lastReply = DateTime.UtcNow;
        var receive = ReceiveLoop(udp, sessionCts.Token);
        var nextHello = DateTime.MinValue;
        while (!ct.IsCancellationRequested && !receive.IsCompleted)
        {
            if (!Connected && DateTime.UtcNow >= nextHello)
            {
                await SendRelay(RelayProtocol.MakeHello(_secret, DateTimeOffset.UtcNow));
                nextHello = DateTime.UtcNow.AddSeconds(3);
            }
            else if (Connected)
            {
                await SendRelay(new[] { RelayProtocol.KeepAlive });
                if (DateTime.UtcNow - _lastReply > RelayTimeout)
                {
                    Fail("Relay antwortet nicht mehr – verbinde neu ...");
                    break;
                }
            }
            if (!Connected && DateTime.UtcNow - _lastReply > TimeSpan.FromSeconds(12) && LastError.Length == 0)
                Fail($"Relay {_host}:{_agentPort} antwortet nicht. Läuft DDS2Relay, ist UDP {_agentPort} dort offen?");
            foreach (var l in _links.Values.Where(l => DateTime.UtcNow - l.LastSeen > LinkIdle).ToList()) RemoveLink(l.Session);
            try { await Task.Delay(KeepAliveEvery, ct); } catch (OperationCanceledException) { break; }
        }
        sessionCts.Cancel();
        try { await receive; } catch (OperationCanceledException) { }
    }

    async Task ReceiveLoop(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (SocketException) { continue; }
            if (!r.RemoteEndPoint.Equals(_relay) || r.Buffer.Length == 0) continue;
            _lastReply = DateTime.UtcNow;
            var p = r.Buffer;
            switch (p[0])
            {
                case RelayProtocol.Welcome when RelayProtocol.ParseWelcome(p, out var g, out var q, out _):
                    RelayGamePort = g;
                    RelayQueryPort = q;
                    if (!Connected)
                    {
                        Connected = true;
                        LastError = "";
                        Status?.Invoke($"Über Relay erreichbar: Mitspieler verbinden sich mit {_host}:{g}.", false);
                    }
                    break;
                case RelayProtocol.Denied:
                    Fail("Relay lehnt ab: " + System.Text.Encoding.UTF8.GetString(p, 1, p.Length - 1));
                    break;
                case RelayProtocol.Stats when p.Length == 3:
                    Clients = p[1] | (p[2] << 8);
                    break;
                case RelayProtocol.Close when RelayProtocol.ParseClose(p, out var closed):
                    RemoveLink(closed);
                    break;
                case RelayProtocol.Data when RelayProtocol.ParseData(p, out var channel, out var session, out var payload):
                    var link = _links.GetOrAdd(session, id => OpenLink(id, channel));
                    link.LastSeen = DateTime.UtcNow;
                    try { await link.Socket.SendAsync(payload.ToArray(), channel == RelayProtocol.ChannelGame ? _localGame : _localQuery, ct); }
                    catch (SocketException) { }
                    break;
            }
        }
    }

    Link OpenLink(uint session, byte channel)
    {
        var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        RelayProtocol.IgnoreConnReset(u.Client);
        var link = new Link { Socket = u, Channel = channel, Session = session };
        _ = Task.Run(() => LinkLoop(link));
        return link;
    }

    // Antworten des Spiels an diesen Mitspieler zurück zum Relay
    async Task LinkLoop(Link link)
    {
        while (!link.Cts.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await link.Socket.ReceiveAsync(link.Cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            link.LastSeen = DateTime.UtcNow;
            await SendRelay(RelayProtocol.MakeData(link.Channel, link.Session, r.Buffer));
        }
    }

    async Task SendRelay(byte[] data)
    {
        var udp = _udp;
        var relay = _relay;
        if (udp is null || relay is null) return;
        try { await udp.SendAsync(data, relay); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    void RemoveLink(uint session)
    {
        if (_links.TryRemove(session, out var l)) l.Dispose();
    }

    void CloseAllLinks()
    {
        foreach (var id in _links.Keys.ToList()) RemoveLink(id);
    }

    void Fail(string message)
    {
        var wasConnected = Connected;
        Connected = false;
        if (message == LastError && !wasConnected) return;
        LastError = message;
        Status?.Invoke(message, true);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _run?.Wait(2000); } catch (AggregateException) { }
        CloseAllLinks();
        _cts.Dispose();
    }
}
