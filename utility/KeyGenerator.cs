// KeyGenerator — BepInEx plugin
// Creates fixed key files under Keys\ and restores the Custom\Certificates\
// folder from an embedded snapshot, on startup, if they don't already exist.

using System;
using System.IO;
using System.Reflection;
using BepInEx;

namespace VaMUtility
{
    [BepInPlugin("com.vam.keygenerator", "KeyGenerator", "1.0.0")]
    public class KeyGeneratorPlugin : BaseUnityPlugin
    {
        struct KeyFile { public string RelativePath; public string Content; }

        static readonly KeyFile[] Files =
        {
            new KeyFile { RelativePath = "Keys\\1.21\\key.json",             Content = "{ \n   \"c91927\" : \"true\"\n}" },
            new KeyFile { RelativePath = "Keys\\pluginidea\\environment.txt", Content = "3d16808lK323ma/TT3FViDyzNJxYmw==" },
        };

        void Awake()
        {
            foreach (var file in Files)
            {
                try
                {
                    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                    if (!File.Exists(fullPath))
                    {
                        File.WriteAllText(fullPath, file.Content);
                        Logger.LogInfo("KeyGenerator: created " + fullPath);
                    }
                    else
                    {
                        Logger.LogInfo("KeyGenerator: " + fullPath + " already exists, skipping.");
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError("KeyGenerator: " + e);
                }
            }

            RestoreCertificates();
        }

        void RestoreCertificates()
        {
            try
            {
                string certDir = Path.Combine(Directory.GetCurrentDirectory(), "Custom\\Certificates");
                Directory.CreateDirectory(certDir);

                int created = 0, skipped = 0;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("certificates_manifest.txt"))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        int sep = line.IndexOf('|');
                        if (sep < 0) continue;

                        string name = line.Substring(0, sep);
                        string b64  = line.Substring(sep + 1);
                        string path = Path.Combine(certDir, name);

                        if (File.Exists(path)) { skipped++; continue; }

                        byte[] bytes = Convert.FromBase64String(b64);
                        File.WriteAllBytes(path, bytes);
                        created++;
                    }
                }

                Logger.LogInfo($"KeyGenerator: Certificates restore — {created} created, {skipped} already present.");
            }
            catch (Exception e)
            {
                Logger.LogError("KeyGenerator: RestoreCertificates failed: " + e);
            }
        }
    }
}
