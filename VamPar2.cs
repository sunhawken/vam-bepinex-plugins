using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace VamPar2
{
    [BepInPlugin("vam.par2creator", "Par2Creator", "1.1.0")]
    public class Par2CreatorPlugin : BaseUnityPlugin
    {
        private ConfigEntry<int> _redundancy;

        private static readonly Regex VolRegex = new Regex(
            @"^(.+)\.vol\d{4}\+\d{2}\.par2$", RegexOptions.IgnoreCase);

        private void Start()
        {
            _redundancy = Config.Bind<int>("General", "RedundancyPercent", 5,
                "Recovery block redundancy percentage (1-50). Default 5 = 5%.");
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), "AddonPackages");
            int redPct = Math.Max(1, Math.Min(50, _redundancy.Value));
            Logger.LogInfo("[Par2] Starting — scanning AddonPackages on background thread");
            Thread thread = new Thread(() => Run(root, redPct));
            thread.IsBackground = true;
            thread.Start();
        }

        private void Run(string root, int redundancyPct)
        {
            ReconcileMovedFiles(root);

            Logger.LogInfo("[Par2] Scanning AddonPackages...");
            string[] files = Directory.GetFiles(root, "*.var", SearchOption.AllDirectories);
            Logger.LogInfo($"[Par2] Found {files.Length} .var packages  redundancy={redundancyPct}%");
            int created = 0, skipped = 0, failed = 0;
            foreach (string path in files)
            {
                if (File.Exists(path + ".par2") &&
                    Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".vol*.par2").Length != 0)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    Logger.LogInfo("[Par2] " + Path.GetFileName(path));
                    Par2Writer.Create(path, redundancyPct);
                    created++;
                }
                catch (Exception ex)
                {
                    Logger.LogError("[Par2] FAILED " + Path.GetFileName(path) + ": " + ex.Message);
                    failed++;
                    TryDelete(path + ".par2");
                    foreach (string v in Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".vol*.par2"))
                        TryDelete(v);
                }
            }
            Logger.LogInfo($"[Par2] Finished — Created:{created}  Skipped:{skipped}  Failed:{failed}");
        }

        // Finds .par2 / .volNNNN+NN.par2 sets whose source .var is no longer in the
        // same folder (because it was moved/reorganized within AddonPackages), and
        // relocates the whole recovery set to wherever that .var now lives.
        private void ReconcileMovedFiles(string root)
        {
            string[] par2Files;
            try { par2Files = Directory.GetFiles(root, "*.par2", SearchOption.AllDirectories); }
            catch (Exception ex) { Logger.LogError("[Par2] Reconcile scan failed: " + ex.Message); return; }

            // Group sibling recovery files by (directory, sourceVarFileName)
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in par2Files)
            {
                string dir = Path.GetDirectoryName(f);
                string name = Path.GetFileName(f);

                string baseName;
                var m = VolRegex.Match(name);
                if (m.Success) baseName = m.Groups[1].Value;
                else if (name.EndsWith(".par2", StringComparison.OrdinalIgnoreCase)) baseName = name.Substring(0, name.Length - 5);
                else continue;

                string key = dir + "|" + baseName;
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
                list.Add(f);
            }

            int moved = 0, missing = 0, ambiguous = 0;
            foreach (var kv in groups)
            {
                int sep = kv.Key.IndexOf('|');
                string dir = kv.Key.Substring(0, sep);
                string baseName = kv.Key.Substring(sep + 1);
                string varPath = Path.Combine(dir, baseName);

                // ".disabled" is just a renamed .var (VaM's "disable without deleting"
                // convention) — same file content, so treat it as the same source.
                if (File.Exists(varPath) || File.Exists(varPath + ".disabled")) continue;

                string[] matches;
                try
                {
                    string[] active   = Directory.GetFiles(root, baseName, SearchOption.AllDirectories);
                    string[] disabled = Directory.GetFiles(root, baseName + ".disabled", SearchOption.AllDirectories);
                    matches = new string[active.Length + disabled.Length];
                    active.CopyTo(matches, 0);
                    disabled.CopyTo(matches, active.Length);
                }
                catch { continue; }

                if (matches.Length == 0)
                {
                    missing++; // source deleted entirely — leave the orphaned recovery set alone
                    continue;
                }
                if (matches.Length > 1)
                {
                    Logger.LogWarning($"[Par2] Multiple candidates for moved file '{baseName}' — skipping auto-relocate (ambiguous).");
                    ambiguous++;
                    continue;
                }

                string newDir = Path.GetDirectoryName(matches[0]);
                if (string.Equals(newDir, dir, StringComparison.OrdinalIgnoreCase)) continue;

                bool ok = true;
                foreach (string f in kv.Value)
                {
                    string dest = Path.Combine(newDir, Path.GetFileName(f));
                    try
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(f, dest);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("[Par2] Failed moving " + f + " -> " + dest + ": " + ex.Message);
                        ok = false;
                    }
                }
                if (ok)
                {
                    Logger.LogInfo($"[Par2] Relocated recovery set for '{baseName}' -> {newDir}");
                    moved++;
                }
            }

            if (moved > 0 || missing > 0 || ambiguous > 0)
                Logger.LogInfo($"[Par2] Reconcile: {moved} relocated, {missing} orphaned (source deleted), {ambiguous} ambiguous.");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }

    internal static class Par2Writer
    {
        private const int BLOCK = 524288;
        private static readonly byte[] MAGIC   = Encoding.ASCII.GetBytes("PAR2\0PKT");
        private static readonly byte[] T_MAIN  = Type16("PAR 2.0\0Main\0\0\0\0");
        private static readonly byte[] T_FDESC = Type16("PAR 2.0\0FileDesc");
        private static readonly byte[] T_IFSC  = Type16("PAR 2.0\0IFSC\0\0\0\0");
        private static readonly byte[] T_RECV  = Type16("PAR 2.0\0RecvSlic");
        private static readonly byte[] T_CREATE= Type16("PAR 2.0\0Creator\0");

        private static byte[] Type16(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            if (b.Length != 16) throw new Exception("type must be 16 bytes");
            return b;
        }

        public static void Create(string path, int redundancyPct)
        {
            long length = new FileInfo(path).Length;
            string fileName = Path.GetFileName(path);
            byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
            int k   = (int)Math.Max(1L, (length + BLOCK - 1) / BLOCK);
            int nr  = Math.Max(1, Math.Min(65534, (int)Math.Ceiling((double)(k * redundancyPct) / 100.0)));

            byte[][] blockHashes = new byte[k][];
            uint[]   blockCrcs   = new uint[k];
            byte[][] recvSlices  = new byte[nr][];
            for (int i = 0; i < nr; i++) recvSlices[i] = new byte[BLOCK];

            byte[] buf = new byte[BLOCK];
            byte[] first16k, fileHash;
            using (MD5 md5 = MD5.Create())
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    int p16k = (int)Math.Min(16384L, length);
                    byte[] head = new byte[p16k];
                    ReadFull(fs, head, p16k);
                    first16k = md5.ComputeHash(head);
                    fs.Seek(0, SeekOrigin.Begin);
                    fileHash = md5.ComputeHash(fs);
                    fs.Seek(0, SeekOrigin.Begin);
                    for (int j = 0; j < k; j++)
                    {
                        int n = ReadFull(fs, buf, BLOCK);
                        if (n < BLOCK) Array.Clear(buf, n, BLOCK - n);
                        blockHashes[j] = md5.ComputeHash(buf);
                        blockCrcs[j]   = Crc32.Compute(buf, BLOCK);
                        for (int r = 0; r < nr; r++)
                            GF16.MulXor(GF16.Pow((int)((long)(r + 1) * j % 65535)), buf, recvSlices[r]);
                    }
                }
            }

            byte[] fileId;
            using (MD5 md5 = MD5.Create())
            {
                var ms = new MemoryStream(24 + nameBytes.Length);
                ms.Write(LE64((ulong)length), 0, 8);
                ms.Write(fileHash, 0, 16);
                ms.Write(nameBytes, 0, nameBytes.Length);
                fileId = md5.ComputeHash(ms.ToArray());
            }
            byte[] setId = Guid.NewGuid().ToByteArray();

            using (MD5 md5 = MD5.Create())
            using (FileStream s = File.Create(path + ".par2"))
            {
                WritePacket(s, md5, setId, T_MAIN,   MainBody(k, nr, fileId));
                WritePacket(s, md5, setId, T_FDESC,  FdescBody(fileId, fileHash, first16k, length, nameBytes));
                WritePacket(s, md5, setId, T_IFSC,   IfscBody(fileId, blockHashes, blockCrcs));
                WritePacket(s, md5, setId, T_CREATE, Pad4(Encoding.ASCII.GetBytes("VaM-Par2Creator")));
            }

            using (MD5 md5v = MD5.Create())
            using (FileStream sv = File.Create(path + $".vol0000+{nr:D2}.par2"))
            {
                byte[] pkt = new byte[BLOCK + 4];
                for (int l = 0; l < nr; l++)
                {
                    Buffer.BlockCopy(LE32((uint)(l + 1)), 0, pkt, 0, 4);
                    Buffer.BlockCopy(recvSlices[l], 0, pkt, 4, BLOCK);
                    WritePacket(sv, md5v, setId, T_RECV, pkt);
                }
            }
        }

        private static byte[] MainBody(int k, int nr, byte[] fileId)
        {
            var ms = new MemoryStream(28);
            ms.Write(LE64(BLOCK), 0, 8);
            ms.Write(LE32((uint)nr), 0, 4);
            ms.Write(fileId, 0, 16);
            return ms.ToArray();
        }

        private static byte[] FdescBody(byte[] fileId, byte[] fileHash, byte[] first16k, long len, byte[] nameBytes)
        {
            var ms = new MemoryStream(56 + nameBytes.Length + 4);
            ms.Write(fileId, 0, 16);
            ms.Write(fileHash, 0, 16);
            ms.Write(first16k, 0, 16);
            ms.Write(LE64((ulong)len), 0, 8);
            byte[] n = Pad4(nameBytes);
            ms.Write(n, 0, n.Length);
            return ms.ToArray();
        }

        private static byte[] IfscBody(byte[] fileId, byte[][] hashes, uint[] crcs)
        {
            var ms = new MemoryStream(16 + hashes.Length * 20);
            ms.Write(fileId, 0, 16);
            for (int i = 0; i < hashes.Length; i++)
            {
                ms.Write(hashes[i], 0, 16);
                ms.Write(LE32(crcs[i]), 0, 4);
            }
            return ms.ToArray();
        }

        private static void WritePacket(Stream s, MD5 md5, byte[] setId, byte[] type, byte[] body)
        {
            byte[] inner = new byte[32 + body.Length];
            Buffer.BlockCopy(setId, 0, inner, 0, 16);
            Buffer.BlockCopy(type,  0, inner, 16, 16);
            Buffer.BlockCopy(body,  0, inner, 32, body.Length);
            byte[] pktMd5 = md5.ComputeHash(inner);
            s.Write(MAGIC, 0, 8);
            s.Write(LE64((ulong)(64 + body.Length)), 0, 8);
            s.Write(pktMd5, 0, 16);
            s.Write(setId,  0, 16);
            s.Write(type,   0, 16);
            s.Write(body,   0, body.Length);
        }

        private static int ReadFull(Stream s, byte[] buf, int count)
        {
            int total = 0, n;
            while (total < count && (n = s.Read(buf, total, count - total)) > 0)
                total += n;
            return total;
        }

        private static byte[] LE64(ulong v) { return BitConverter.GetBytes(v); }
        private static byte[] LE64(long  v) { return BitConverter.GetBytes(v); }
        private static byte[] LE32(uint  v) { return BitConverter.GetBytes(v); }
        private static byte[] Pad4(byte[] b)
        {
            int pad = (4 - b.Length % 4) % 4;
            if (pad == 0) return b;
            byte[] outb = new byte[b.Length + pad];
            Buffer.BlockCopy(b, 0, outb, 0, b.Length);
            return outb;
        }
    }

    internal static class GF16
    {
        public const int ORDER = 65535;
        private static readonly ushort[] Exp = new ushort[131070];
        private static readonly ushort[] Log = new ushort[65536];

        static GF16()
        {
            int v = 1;
            for (int i = 0; i < 65535; i++)
            {
                Exp[i] = Exp[i + 65535] = (ushort)v;
                Log[v] = (ushort)i;
                v <<= 1;
                if ((v & 0x10000) != 0) v ^= 0x1100B;
            }
        }

        public static ushort Pow(int e) { return Exp[(e % 65535 + 65535) % 65535]; }

        public static void MulXor(ushort scalar, byte[] src, byte[] dst)
        {
            if (scalar == 0) return;
            int logS = Log[scalar];
            for (int i = 0; i < src.Length; i += 2)
            {
                ushort w = (ushort)(src[i] | (src[i + 1] << 8));
                if (w == 0) continue;
                ushort r = Exp[logS + Log[w]];
                dst[i]     ^= (byte)r;
                dst[i + 1] ^= (byte)(r >> 8);
            }
        }
    }

    internal static class Crc32
    {
        private static readonly uint[] Table = new uint[256];

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                    c = ((c & 1) != 0) ? ((c >> 1) ^ 0xEDB88320u) : (c >> 1);
                Table[i] = c;
            }
        }

        public static uint Compute(byte[] data, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < length; i++)
                crc = (crc >> 8) ^ Table[(crc ^ data[i]) & 0xFF];
            return ~crc;
        }
    }
}
