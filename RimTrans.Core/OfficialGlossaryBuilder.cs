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
    /// 构建结果按 Data 目录指纹（各 tar 的大小+修改时间）缓存到 %APPDATA%\RimTranslator\official-glossary-<语言>.json，
    /// DLC 未变化时秒级加载，无需重复解析全部 tar。
    /// </summary>
    public static class OfficialGlossaryBuilder
    {
        public static List<GlossaryEntry> Build(string dataDir, string language)
        {
            string? cacheFile = null, fingerprint = null;
            try
            {
                fingerprint = ComputeFingerprint(dataDir, language);
                cacheFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimTranslator",
                    $"official-glossary-{language}.json");
                if (File.Exists(cacheFile))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cacheFile));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("fp", out var fp) && fp.GetString() == fingerprint &&
                        root.TryGetProperty("entries", out var arr))
                    {
                        var cached = new List<GlossaryEntry>();
                        foreach (var el in arr.EnumerateArray())
                            cached.Add(new GlossaryEntry
                            {
                                En = el.GetProperty("en").GetString() ?? "",
                                Translation = el.GetProperty("zh").GetString() ?? "",
                                Source = "官方",
                            });
                        return cached;
                    }
                }
            }
            catch { }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dataDir))
            foreach (var dlc in Directory.EnumerateDirectories(dataDir))
            {
                string languagesDir = Path.Combine(dlc, "Languages");
                if (!Directory.Exists(languagesDir)) continue;

                string? targetDir = FindLanguageDir(languagesDir, language);
                string? englishDir = FindLanguageDir(languagesDir, "English");
                if (targetDir == null || englishDir == null) continue;

                foreach (var (relPath, content) in EnumerateLanguageFiles(targetDir))
                {
                    string? english = englishDir == null ? null : ReadCounterpart(englishDir, relPath);
                    if (english == null) continue;
                    ParsePairs(english, content, map);
                }
            }
            var result = map.Select(kv => new GlossaryEntry { En = kv.Key, Translation = kv.Value, Source = "官方" })
                .OrderBy(x => x.En)
                .ToList();

            // 写缓存（失败不影响结果）
            try
            {
                if (cacheFile != null && fingerprint != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                    var payload = new
                    {
                        fp = fingerprint,
                        entries = result.Select(e => new { en = e.En, zh = e.Translation }),
                    };
                    File.WriteAllText(cacheFile, System.Text.Json.JsonSerializer.Serialize(payload));
                }
            }
            catch { }
            return result;
        }

        /// <summary>Data 目录指纹：全部语言 tar/目录的大小+修改时间哈希。DLC 更新或换语言后自动失效。</summary>
        private static string ComputeFingerprint(string dataDir, string language)
        {
            long hash = 17;
            foreach (var dlc in Directory.EnumerateDirectories(dataDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string languagesDir = Path.Combine(dlc, "Languages");
                if (!Directory.Exists(languagesDir)) continue;
                string? target = FindLanguageDir(languagesDir, language);
                string? english = FindLanguageDir(languagesDir, "English");
                foreach (var dir in new[] { target, english })
                {
                    if (dir == null) continue;
                    hash = hash * 31 + dir.Length;
                    if (dir.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(dir))
                        {
                            var fi = new FileInfo(dir);
                            hash = hash * 31 + fi.Length;
                            hash = hash * 31 + fi.LastWriteTimeUtc.Ticks;
                        }
                    }
                    else
                    {
                        foreach (var f in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories)
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        {
                            var fi = new FileInfo(f);
                            hash = hash * 31 + f.Length;
                            hash = hash * 31 + fi.Length;
                            hash = hash * 31 + fi.LastWriteTimeUtc.Ticks;
                        }
                    }
                }
            }
            return $"{language}-{hash}";
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
                        // tar 本身可直接读取；优先已解压目录
                        string extracted = entry[..^4];
                        if (Directory.Exists(extracted)) return extracted;
                        return entry; // 直接返回 tar 路径，EnumerateTar 会解析
                    }
                    if (Directory.Exists(entry))
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
            if (englishDir.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                return ReadTarEntry(englishDir, relPath.Replace('\\', '/'));
            }
            if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8);
            return null;
        }

        private static string? ReadTarEntry(string tarPath, string relPath)
        {
            using var fs = File.OpenRead(tarPath);
            using var tr = new TarReader(fs);
            while (tr.GetNextEntry() is { } e)
            {
                if (e.EntryType != TarEntryType.RegularFile) continue;
                string name = e.Name.StartsWith("Languages/", StringComparison.OrdinalIgnoreCase)
                    ? e.Name["Languages/".Length..]
                    : e.Name;
                if (name.Equals(relPath, StringComparison.OrdinalIgnoreCase))
                {
                    using var sr = new StreamReader(e.DataStream!, Encoding.UTF8);
                    return sr.ReadToEnd();
                }
            }
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
                    if (en.Length < 2 || en == "-" || en == "," || en == "x") continue;
                    if (en.EndsWith(",") || en.EndsWith(":") || en.StartsWith("(") && en.EndsWith(")")) continue;
                    if (tgt.TryGetValue(key, out var tr) && !string.IsNullOrWhiteSpace(tr) && tr != "TODO" && tr.Length <= 300 && tr != en)
                        map.TryAdd(en, tr);
                }
            }
            catch
            {
            }
        }
    }
}
