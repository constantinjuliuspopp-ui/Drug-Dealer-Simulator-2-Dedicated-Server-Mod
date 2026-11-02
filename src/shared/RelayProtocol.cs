using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DDS2Mod.Shared;

/// <summary>
/// Protokoll zwischen Relay-Server (DDS2Relay) und Relay-Agent (im Launcher des Hosts), UDP, binär.
/// Der Agent baut die Verbindung von innen auf – der Host braucht keine Portfreigabe.
///   HELLO     Agent→Relay  [01][ver][unix-sekunden i64][nonce 16][HMAC-SHA256(schlüssel, bytes 0..25)]
///   WELCOME   Relay→Agent  [02][spielport u16][abfrageport u16][max. clients u16]
///   DENIED    Relay→Agent  [03][grund utf8]
///   KEEPALIVE Agent→Relay  [04]
///   STATS     Relay→Agent  [05][clients u16]
///   DATA      beide        [10][kanal][sitzung u32][nutzdaten]   kanal 0 = Spiel, 1 = Abfrage-Port
///   CLOSE     Relay→Agent  [11][sitzung u32]
/// Alle Zahlen little-endian. Das Relay versteht weder Spiel- noch Abfrage-Pakete, es reicht sie nur durch.
/// </summary>
public static class RelayProtocol
{
    public const byte Hello = 0x01, Welcome = 0x02, Denied = 0x03, KeepAlive = 0x04, Stats = 0x05, Data = 0x10, Close = 0x11;
    public const byte Version = 1;
    public const byte ChannelGame = 0, ChannelQuery = 1;
    public const int DefaultAgentPort = 7779;
    public const int HelloLength = 1 + 1 + 8 + 16 + 32;
    public const int DataHeader = 6;
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    public static byte[] MakeHello(string secret, DateTimeOffset now, byte[]? nonce = null)
    {
        var p = new byte[HelloLength];
        p[0] = Hello;
        p[1] = Version;
        BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(2), now.ToUnixTimeSeconds());
        (nonce ?? RandomNumberGenerator.GetBytes(16)).CopyTo(p, 10);
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), p.AsSpan(0, 26), p.AsSpan(26));
        return p;
    }

    /// <summary>Prüft Schlüssel und Uhrzeit; nonce dient dem Relay zum Erkennen wiederholter Pakete.</summary>
    public static bool CheckHello(ReadOnlySpan<byte> p, string secret, DateTimeOffset now, out string nonce, out string reason)
    {
        nonce = "";
        reason = "";
        if (p.Length != HelloLength || p[0] != Hello) { reason = "ungültiges Paket"; return false; }
        if (p[1] != Version) { reason = $"Relay-Protokoll {p[1]} statt {Version} – Launcher und Relay auf dieselbe Version bringen"; return false; }
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), p[..26], mac);
        if (!CryptographicOperations.FixedTimeEquals(mac, p[26..])) { reason = "falscher Relay-Schlüssel"; return false; }
        var sent = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(p[2..]));
        if ((now - sent).Duration() > MaxClockSkew) { reason = "Uhrzeit von Host und Relay weicht mehr als 5 Minuten ab"; return false; }
        nonce = Convert.ToHexString(p.Slice(10, 16));
        return true;
    }

    public static byte[] MakeWelcome(int gamePort, int queryPort, int maxClients)
    {
        var p = new byte[7];
        p[0] = Welcome;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)gamePort);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(3), (ushort)queryPort);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(5), (ushort)maxClients);
        return p;
    }

    public static bool ParseWelcome(ReadOnlySpan<byte> p, out int gamePort, out int queryPort, out int maxClients)
    {
        gamePort = queryPort = maxClients = 0;
        if (p.Length != 7 || p[0] != Welcome) return false;
        gamePort = BinaryPrimitives.ReadUInt16LittleEndian(p[1..]);
        queryPort = BinaryPrimitives.ReadUInt16LittleEndian(p[3..]);
        maxClients = BinaryPrimitives.ReadUInt16LittleEndian(p[5..]);
        return true;
    }

    public static byte[] MakeDenied(string reason) => new[] { Denied }.Concat(Encoding.UTF8.GetBytes(reason)).ToArray();

    public static byte[] MakeStats(int clients)
    {
        var p = new byte[3];
        p[0] = Stats;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)Math.Min(clients, ushort.MaxValue));
        return p;
    }

    public static byte[] MakeData(byte channel, uint session, ReadOnlySpan<byte> payload)
    {
        var p = new byte[DataHeader + payload.Length];
        p[0] = Data;
        p[1] = channel;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), session);
        payload.CopyTo(p.AsSpan(DataHeader));
        return p;
    }

    public static bool ParseData(byte[] p, out byte channel, out uint session, out ArraySegment<byte> payload)
    {
        channel = 0;
        session = 0;
        payload = default;
        if (p.Length < DataHeader || p[0] != Data || p[1] > ChannelQuery) return false;
        channel = p[1];
        session = BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2));
        payload = new ArraySegment<byte>(p, DataHeader, p.Length - DataHeader);
        return true;
    }

    public static byte[] MakeClose(uint session)
    {
        var p = new byte[5];
        p[0] = Close;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(1), session);
        return p;
    }

    public static bool ParseClose(ReadOnlySpan<byte> p, out uint session)
    {
        session = 0;
        if (p.Length != 5 || p[0] != Close) return false;
        session = BinaryPrimitives.ReadUInt32LittleEndian(p[1..]);
        return true;
    }

    /// <summary>"host" oder "host:port" (Port = Agent-Port des Relays).</summary>
    public static bool TryParseAddress(string text, out string host, out int port)
    {
        host = "";
        port = DefaultAgentPort;
        var t = text.Trim();
        var colon = t.LastIndexOf(':');
        if (colon > 0 && t.IndexOf(':') == colon)
        {
            if (!int.TryParse(t[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
            t = t[..colon];
        }
        host = t;
        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    /// <summary>Windows: ICMP "Port nicht erreichbar" soll den Empfang eines UDP-Sockets nicht abbrechen.</summary>
    public static void IgnoreConnReset(System.Net.Sockets.Socket s)
    {
        if (OperatingSystem.IsWindows()) s.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, new byte[4], null);
    }
}
