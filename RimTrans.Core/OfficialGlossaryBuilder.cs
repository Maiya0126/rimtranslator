using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RimTrans.Core
{
    /// <summary>
    /// 从游戏官方翻译（Data\<DLC>\Languages\<语言>，tar 或已解压目录）构建 EN→译文 官方术语库。
    /// 只读 Keyed 与 DefInjected 的"完整句/词"条目；带 {0} 占位符的也保留（AI 翻译时占位符必须原样）。
    /// </summary>
    public static class OfficialGlossaryBuilder
    {
        public static List<GlossaryEntry> Build(string dataDir, string language)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dataDir)) return new();

            foreach (var dlc in Directory.EnumerateDirectories(dataDir))
            {
                string languagesDir = Path.Combine(dlc, "Languages");
                if (!Directory.Exists(languagesDir)) continue;

                string? targetDir = FindLanguageDir(languagesDir, language);
                string? englishDir = FindLanguageDir(languagesDir, "English");

                foreach (var (relPath, content) in EnumerateLanguageFiles(targetDir))
                {
                    string? english = englishDir == null ? null : ReadCounterpart(englishDir, relPath);
                    if (english == null) continue;
                    ParsePairs(english, content, map);
                }
            }
            return map.Select(kv => new GlossaryEntry { En = kv.Key, Translation = kv.Value, Source = "官方" })
                .OrderBy(x => x.En)
                .ToList();
        }

        private static string? FindLanguageDir(string languagesDir, string language)
        {
            string direct = Path.Combine(languagesDir, language);
            if (Directory.Exists(direct)) return direct;
            foreach (var entry in Directory.EnumerateFileSystemEntries(languagesDir))
            {
                string n = Path.GetFileName(entry);
                if (n.StartsWith(language, StringComparison.OrdinalIgnoreCase) &&
                    (n.Length == language.Length || n[language.Length] == ' ' || n[language.Length] == '('))
                {
                    if (n.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                    {
                        string extracted = entry[..^4];
                        if (Directory.Exists(extracted)) return extracted;
                    }
                    else if (Directory.Exists(entry))
                    {
                        return entry;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<(string rel, string content)> EnumerateLanguageFiles(string? dir)
        {
            if (dir == null) yield break;
            if (dir.EndsWith(".tar"))
            {
                foreach (var (rel, content) in EnumerateTar(dir)) yield return (rel, content);
                yield break;
            }
            foreach (var f in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
                yield return (Path.GetRelativePath(dir, f), File.ReadAllText(f, Encoding.UTF8));
        }

        private static IEnumerable<(string rel, string content)> EnumerateTar(string tarPath)
        {
            using var fs = File.OpenRead(tarPath);
            using var tr = new TarReader(fs);
            while (tr.GetNextEntry() is { } e)
            {
                if (e.EntryType != TarEntryType.RegularFile || !e.Name.EndsWith(".xml")) continue;
                using var sr = new StreamReader(e.DataStream!, Encoding.UTF8);
                string rel = e.Name.StartsWith("Languages/", StringComparison.OrdinalIgnoreCase)
                    ? e.Name["Languages/".Length..]
                    : e.Name;
                yield return (rel, sr.ReadToEnd());
            }
        }

        private static string? ReadCounterpart(string englishDir, string relPath)
        {
            string p = Path.Combine(englishDir, relPath);
            if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8);
            return null;
        }

        private static void ParsePairs(string englishXml, string targetXml, Dictionary<string, string> map)
        {
            try
            {
                var enRoot = XDocument.Parse(englishXml).Root;
                var tgtRoot = XDocument.Parse(targetXml).Root;
                if (enRoot == null || tgtRoot == null) return;

                var tgt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in tgtRoot.Elements())
                    tgt[el.Name.ToString()] = el.Value;

                foreach (var el in enRoot.Elements())
                {
                    string key = el.Name.ToString();
                    string en = el.Value.Trim();
                    if (en.Length == 0 || en == "TODO" || en.Length > 200) continue;
                    if (tgt.TryGetValue(key, out var tr) && !string.IsNullOrWhiteSpace(tr) && tr != "TODO" && tr.Length <= 300)
                        map.TryAdd(en, tr);
                }
            }
            catch
            {
            }
        }
    }
}
