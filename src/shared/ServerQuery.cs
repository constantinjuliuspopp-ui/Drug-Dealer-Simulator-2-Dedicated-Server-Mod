using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace DDS2Mod.Shared;

/// <summary>Was der Server auf dem Abfrage-Port meldet.</summary>
public sealed record ServerInfo(
    string Name, string ModVersion, int Protocol, string GameBuild, string State,
    int Players, int MaxPlayers, bool HasPassword, string World)
{
    /// <summary>Gemessene Antwortzeit der Abfrage in ms (nur beim Client gesetzt).</summary>
    public int Ping { get; init; } = -1;
    public string Challenge { get; init; } = "";
    public bool IsFull => Players >= MaxPlayers;
    public bool IsJoinable => State is "world" or "lobby";
}

/// <summary>
/// Abfrage-Port (UDP, Standard Spielport + 1): liefert Name, Spielerzahl, Version, Passwortpflicht und
/// prüft ein Passwort per Challenge-Response, ohne dass es im Klartext übertragen wird.
/// Protokoll (UTF-8, Zeilen mit key=value):
///   Anfrage  "DDS2Q|1|&lt;nonce&gt;" auf 128 Byte aufgefüllt (kleine Anfragen werden ignoriert: kein Verstärker)
///   Antwort  "DDS2R|1|&lt;nonce&gt;\nname=…\nmod=…\n…\nchallenge=&lt;hex&gt;"
///   Passwort "DDS2P|1|&lt;challenge&gt;|&lt;HMAC-SHA256(NetworkVersionTag(pw), challenge)&gt;" → "DDS2A|1|&lt;challenge&gt;|ok|wrong"
/// </summary>
public static class ServerQuery
{
    public const int ProtocolVersion = 1;
    const int MinRequestSize = 128;

    public static int DefaultQueryPort(int gamePort) => gamePort == 65535 ? 65534 : gamePort + 1;

    // ---------------- Client ----------------

    /// <summary>Fragt den Server ab. null = keine Antwort (Server aus, Port zu, falsche Adresse).</summary>
    public static async Task<ServerInfo?> QueryAsync(IPEndPoint server, int attempts = 3, int timeoutMs = 800, CancellationToken ct = default)
    {
        using var udp = new UdpClient(server.AddressFamily);
        for (var i = 0; i < attempts; i++)
        {
            var nonce = RandomHex(8);
            var sw = Stopwatch.StartNew();
            await udp.SendAsync(Pad($"DDS2Q|{ProtocolVersion}|{nonce}"), server, ct);
            var reply = await ReceiveAsync(udp, server, timeoutMs, ct);
            if (reply is null) continue;
            var info = ParseInfo(reply, nonce);
            if (info != null) return info with { Ping = (int)sw.ElapsedMilliseconds };
        }
        return null;
    }

    /// <summary>true = Passwort stimmt, false = falsch, null = keine Antwort.</summary>
    public static async Task<bool?> CheckPasswordAsync(IPEndPoint server, string challenge, string password, int timeoutMs = 1500, CancellationToken ct = default)
    {
        using var udp = new UdpClient(server.AddressFamily);
        await udp.SendAsync(Pad($"DDS2P|{ProtocolVersion}|{challenge}|{PasswordProof(password, challenge)}"), server, ct);
        var reply = await ReceiveAsync(udp, server, timeoutMs, ct);
        var parts = reply?.Split('|');
        if (parts is not { Length: 4 } || parts[0] != "DDS2A" || parts[2] != challenge) return null;
        return parts[3] == "ok";
    }

    static async Task<string?> ReceiveAsync(UdpClient udp, IPEndPoint server, int timeoutMs, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                var r = await udp.ReceiveAsync(timeout.Token);
                if (r.RemoteEndPoint.Port == server.Port) return Encoding.UTF8.GetString(r.Buffer);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (SocketException) { return null; } // z. B. ICMP "Port nicht erreichbar"
    }

    internal static ServerInfo? ParseInfo(string reply, string nonce)
    {
        var lines = reply.Split('\n');
        if (lines[0] != $"DDS2R|{ProtocolVersion}|{nonce}") return null;
        var d = lines.Skip(1).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        string S(string k) => d.TryGetValue(k, out var v) ? v : "";
        int I(string k) => int.TryParse(S(k), out var n) ? n : 0;
        return new ServerInfo(S("name"), S("mod"), I("protocol"), S("build"), S("state"), I("players"), I("max"), S("password") == "1", S("world"))
        {
            Challenge = S("challenge"),
        };
    }

    internal static string PasswordProof(string password, string challenge) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(IniOverride.NetworkVersionTag(password)), Encoding.UTF8.GetBytes(challenge)));

    static byte[] Pad(string request)
    {
        var bytes = Encoding.UTF8.GetBytes(request + "\n");
        return bytes.Length >= MinRequestSize ? bytes : bytes.Concat(new byte[MinRequestSize - bytes.Length]).ToArray();
    }

    static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));

    // ---------------- Server ----------------

    /// <summary>Beantwortet Abfragen, bis das Token abgebrochen wird. info() liefert den aktuellen Stand.</summary>
    public sealed class Responder : IDisposable
    {
        readonly UdpClient _udp;
        readonly Func<ServerInfo> _info;
        readonly string _password;
        readonly ConcurrentDictionary<string, DateTime> _challenges = new();
        readonly Dictionary<IPAddress, (DateTime Second, int Count)> _rate = new();

        public int Port { get; }

        public Responder(int port, string password, Func<ServerInfo> info)
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            // Windows: ICMP "Port nicht erreichbar" eines Clients soll den Empfang nicht abbrechen.
            if (OperatingSystem.IsWindows()) _udp.Client.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, new byte[4], null);
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
            _password = password;
            _info = info;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await _udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { return; }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { return; }

                if (r.Buffer.Length < MinRequestSize || !Allow(r.RemoteEndPoint.Address)) continue;
                var reply = Handle(Encoding.UTF8.GetString(r.Buffer).TrimEnd('\0', '\n'));
                if (reply is null) continue;
                try { await _udp.SendAsync(Encoding.UTF8.GetBytes(reply), r.RemoteEndPoint, ct); }
                catch (SocketException) { }
            }
        }

        internal string? Handle(string request)
        {
            var p = request.Split('|');
            if (p.Length < 3 || p[1] != ProtocolVersion.ToString()) return null;
            if (p[0] == "DDS2Q" && p[2].Length <= 32)
            {
                var i = _info();
                var challenge = RandomHex(16);
                foreach (var old in _challenges.Where(c => DateTime.UtcNow - c.Value > TimeSpan.FromSeconds(60)).ToList())
                    _challenges.TryRemove(old.Key, out _);
                if (_challenges.Count < 1000) _challenges[challenge] = DateTime.UtcNow;
                return string.Join('\n',
                    $"DDS2R|{ProtocolVersion}|{p[2]}",
                    $"name={Clean(i.Name)}", $"mod={i.ModVersion}", $"protocol={i.Protocol}", $"build={i.GameBuild}",
                    $"state={i.State}", $"players={i.Players}", $"max={i.MaxPlayers}",
                    $"password={(i.HasPassword ? 1 : 0)}", $"world={Clean(i.World)}", $"challenge={challenge}");
            }
            if (p[0] == "DDS2P" && p.Length == 4 && _challenges.TryRemove(p[2], out _))
            {
                var ok = CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(PasswordProof(_password, p[2])), Encoding.ASCII.GetBytes(p[3]));
                return $"DDS2A|{ProtocolVersion}|{p[2]}|{(ok ? "ok" : "wrong")}";
            }
            return null;
        }

        // Höchstens 10 Anfragen pro Sekunde und Absender.
        bool Allow(IPAddress from)
        {
            var now = DateTime.UtcNow;
            var second = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            if (_rate.Count > 10_000) _rate.Clear();
            var (s, n) = _rate.TryGetValue(from, out var e) ? e : (second, 0);
            if (s != second) (s, n) = (second, 0);
            _rate[from] = (s, n + 1);
            return n < 10;
        }

        static string Clean(string s) => new(s.Replace('\n', ' ').Replace('|', '/').Take(64).ToArray());

        public void Dispose() => _udp.Dispose();
    }
}
