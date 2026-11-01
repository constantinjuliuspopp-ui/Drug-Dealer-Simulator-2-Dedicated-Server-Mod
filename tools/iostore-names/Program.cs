// Minimaler IoStore-Leser (nur lesen): listet Dateien und dumpt Strings ausgewählter Pakete.
// Aufruf: iostore <utoc> <oo2core.dll> <outDir> [pfadRegex]
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

var utoc = args[0]; var dll = args[1]; var outDir = args[2]; var filter = args.Length > 3 ? new Regex(args[3], RegexOptions.IgnoreCase) : null;
Directory.CreateDirectory(outDir);
var lib = NativeLibrary.Load(dll);
var decomp = Marshal.GetDelegateForFunctionPointer<OodleDecompress>(NativeLibrary.GetExport(lib, "OodleLZ_Decompress"));

var t = File.ReadAllBytes(utoc);
int U32(int o) => BitConverter.ToInt32(t, o);
int entries = U32(24), blocks = U32(28), blockEntrySize = U32(32), methods = U32(36), methodLen = U32(40), blockSize = U32(44), dirSize = U32(48);
int seeds = U32(84), noHash = U32(96);
int p = U32(20);
int chunkIdsOff = p; p += entries * 12;
int olOff = p; p += entries * 10;
p += seeds * 4 + noHash * 4;
int blkOff = p; p += blocks * blockEntrySize;
var methodNames = new List<string>();
for (int i = 0; i < methods; i++) { methodNames.Add(Encoding.ASCII.GetString(t, p, methodLen).TrimEnd('\0')); p += methodLen; }
int dirOff = p;
Console.WriteLine($"entries={entries} blocks={blocks} methods={string.Join(",", methodNames)} dirSize={dirSize}");

long BE5(int o) { long v = 0; for (int i = 0; i < 5; i++) v = (v << 8) | t[o + i]; return v; }
long LE(int o, int n) { long v = 0; for (int i = n - 1; i >= 0; i--) v = (v << 8) | t[o + i]; return v; }

// Verzeichnisindex
int q = dirOff;
string ReadFString() { int len = BitConverter.ToInt32(t, q); q += 4; if (len == 0) return ""; if (len < 0) { var s = Encoding.Unicode.GetString(t, q, -len * 2 - 2); q += -len * 2; return s; } var a = Encoding.ASCII.GetString(t, q, len - 1); q += len; return a; }
var mount = ReadFString();
int dn = BitConverter.ToInt32(t, q); q += 4; var dirs = new (uint name, uint child, uint sib, uint file)[dn];
for (int i = 0; i < dn; i++) { dirs[i] = (BitConverter.ToUInt32(t, q), BitConverter.ToUInt32(t, q + 4), BitConverter.ToUInt32(t, q + 8), BitConverter.ToUInt32(t, q + 12)); q += 16; }
int fn = BitConverter.ToInt32(t, q); q += 4; var files = new (uint name, uint next, uint data)[fn];
for (int i = 0; i < fn; i++) { files[i] = (BitConverter.ToUInt32(t, q), BitConverter.ToUInt32(t, q + 4), BitConverter.ToUInt32(t, q + 8)); q += 12; }
int sn = BitConverter.ToInt32(t, q); q += 4; var strs = new string[sn]; for (int i = 0; i < sn; i++) strs[i] = ReadFString();
var paths = new List<(string path, int entry)>();
void Walk(uint d, string prefix)
{
    while (d != uint.MaxValue)
    {
        var e = dirs[d];
        var name = e.name == uint.MaxValue ? "" : strs[e.name];
        var dp = name.Length > 0 ? prefix + name + "/" : prefix;
        for (var f = e.file; f != uint.MaxValue; f = files[f].next) paths.Add((dp + strs[files[f].name], (int)files[f].data));
        if (e.child != uint.MaxValue) Walk(e.child, dp);
        d = e.sib;
    }
}
Walk(0, mount);
File.WriteAllLines(Path.Combine(outDir, "files.txt"), paths.Select(x => x.path));
Console.WriteLine($"{paths.Count} Dateien");
if (filter == null) return;

var ucas = File.OpenRead(Path.ChangeExtension(utoc, ".ucas"));
byte[] ReadChunk(int entry)
{
    long off = BE5(olOff + entry * 10), len = BE5(olOff + entry * 10 + 5);
    var result = new byte[len]; long written = 0;
    for (long b = off / blockSize; written < len; b++)
    {
        int bo = blkOff + (int)b * blockEntrySize;
        long cOff = LE(bo, 5); int cSize = (int)LE(bo + 5, 3); int uSize = (int)LE(bo + 8, 3); int m = t[bo + 11];
        var comp = new byte[(cSize + 15) & ~15]; ucas.Position = cOff; ucas.ReadExactly(comp, 0, comp.Length);
        byte[] raw;
        if (m == 0) raw = comp[..uSize];
        else { raw = new byte[uSize]; var r = decomp(comp, cSize, raw, uSize, 1, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 3); if (r != uSize) throw new Exception($"Oodle {r}!={uSize}"); }
        long start = b == off / blockSize ? off % blockSize : 0;
        long take = Math.Min(uSize - start, len - written);
        Array.Copy(raw, start, result, written, take); written += take;
    }
    return result;
}
foreach (var (path, entry) in paths.Where(x => filter.IsMatch(x.path)))
{
    var data = ReadChunk(entry);
    var s = Encoding.Latin1.GetString(data);
    var words = NameMap(data).Concat(new[] { "----- STRINGS -----" }).Concat(Regex.Matches(s, @"[\x20-\x7e]{3,}").Select(m => m.Value).Distinct());
    var name = path.Replace('/', '_').TrimStart('_');
    File.WriteAllLines(Path.Combine(outDir, name + ".txt"), words);
    Console.WriteLine($"{path}  {data.Length} B");
}

static List<string> NameMap(byte[] d)
{
    var res = new List<string>();
    byte[] magic = { 0x00, 0x00, 0x64, 0xC1, 0x00, 0x00, 0x00, 0x00 };
    int at = -1;
    for (int i = 8; i < Math.Min(d.Length - 8, 4096); i++) if (d.AsSpan(i, 8).SequenceEqual(magic)) { at = i; break; }
    if (at < 0) return res;
    int num = BitConverter.ToInt32(d, at - 8);
    int hdr = at + 8 + num * 8, str = hdr + num * 2;
    for (int i = 0; i < num; i++)
    {
        bool wide = (d[hdr + i * 2] & 0x80) != 0; int len = ((d[hdr + i * 2] & 0x7f) << 8) | d[hdr + i * 2 + 1];
        if (wide) { if ((str & 1) != 0) str++; res.Add(Encoding.Unicode.GetString(d, str, len * 2)); str += len * 2; }
        else { res.Add(Encoding.Latin1.GetString(d, str, len)); str += len; }
    }
    return res;
}

delegate long OodleDecompress(byte[] comp, long compLen, byte[] raw, long rawLen, int fuzz, int crc, int verb, IntPtr a, long b, IntPtr c, IntPtr d, IntPtr e, long f, int phase);
