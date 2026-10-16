using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTrans.Core
{
    public class ModScanner
    {
        public List<ModInfo> Mods { get; } = new();

        public int ScannedDirs { get; private set; }
        public int ParsedOk { get; private set; }
        public int ParsedFail { get; private set; }
        public double ScanSeconds { get; private set; }

        public void ScanAll(IEnumerable<(ModSource source, string rootDir)> sources)
        {
            Mods.Clear();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var (source, rootDir) in sources)
                ScanSource(source, rootDir);
            sw.Stop();
            ScanSeconds = sw.Elapsed.TotalSeconds;
        }

        public void ScanSource(ModSource source, string rootDir)
        {
            if (!Directory.Exists(rootDir)) return;
            foreach (var dir in Directory.EnumerateDirectories(rootDir))
                ScanModDir(dir, source);
        }

        public bool ScanModDir(string modDir, ModSource source)
        {
            ScannedDirs++;
            ModInfo? mod = TryParseMod(modDir);
            if (mod == null) { ParsedFail++; return false; }
            mod.Source = source;
            if (source == ModSource.Workshop)
            {
                string pfid = Path.Combine(modDir, "About", "PublishedFileId.txt");
                if (File.Exists(pfid))
                {
                    try { mod.PublishedFileId = File.ReadAllText(pfid).Trim(); } catch { }
                }
                if (string.IsNullOrEmpty(mod.PublishedFileId))
                {
                    string dirName = Path.GetFileName(modDir);
                    if (dirName.All(char.IsDigit)) mod.PublishedFileId = dirName;
                }
            }
            Mods.Add(mod);
            ParsedOk++;
            return true;
        }

        private static ModInfo? TryParseMod(string modDir)
        {
            string? aboutFile = FindAboutXml(modDir);
            if (aboutFile == null) return null;
            var mod = new ModInfo { RootDir = modDir, Name = Path.GetFileName(modDir) };
            AboutParser.Parse(aboutFile, mod);
            string lf = Path.Combine(modDir, "LoadFolders.xml");
            if (File.Exists(lf))
                mod.LoadFolders = LoadFoldersParser.Parse(lf);
            CollectLanguages(mod);
            return mod;
        }

        internal static string? FindAboutXml(string modDir)
        {
            string direct = Path.Combine(modDir, "About", "About.xml");
            if (File.Exists(direct)) return direct;
            try
            {
                var hits = Directory.EnumerateFiles(modDir, "About.xml", SearchOption.AllDirectories).ToList();
                return hits.Count > 0 ? hits[0] : null;
            }
            catch { return null; }
        }

        private static void CollectLanguages(ModInfo mod)
        {
            var langs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void scanDir(string languagesDir)
            {
                if (!Directory.Exists(languagesDir)) return;
                foreach (var d in Directory.EnumerateDirectories(languagesDir))
                {
                    string n = Path.GetFileName(d);
                    if (!n.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) langs.Add(n);
                }
            }
            scanDir(Path.Combine(mod.RootDir, "Languages"));
            try
            {
                foreach (var vd in Directory.EnumerateDirectories(mod.RootDir))
                {
                    string n = Path.GetFileName(vd);
                    if (n.Length > 2 && n[0] == 'v' && char.IsDigit(n[1])) continue;
                    if (n.Length >= 3 && n[0] == '1' && n[1] == '.' && char.IsDigit(n[2]))
                        scanDir(Path.Combine(vd, "Languages"));
                }
            }
            catch { }
            mod.Languages = langs.ToList();
        }
    }
}
