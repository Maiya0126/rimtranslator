using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using RimTrans.Core.Extraction;

namespace RimTrans.Core.Defs
{
    /// <summary>
    /// 离线版 CleanupDefInjectionsForDefType：生成 DefInjected\&lt;DefType&gt;\&lt;fileName&gt;.xml。
    /// 输出结构对齐 Verse.TranslationFilesCleaner：
    /// 按源文件名分组 → 按 defName 排序 → 每字段 EN 注释 + 译文元素（TODO/已有翻译）→ NEWLINE 注释分隔 → UNUSED 段。
    /// </summary>
    public static class DefInjectedGenerator
    {
        public static ExtractResult Generate(
            ModInfo mod,
            ExtractionOptions options,
            List<OfflineDef> defs,
            Dictionary<string, string> existingInjections,
            IExtractLogger logger)
        {
            var result = new ExtractResult();
            if (defs.Count == 0) return result;

            var languagesDirs = new List<string>();
            string outputBase;
            if (options.Mode == OutputMode.ModDir)
            {
                foreach (var dir in mod.GetContentDirs(options.GameVersion))
                {
                    string l = Path.Combine(dir, "Languages");
                    if (Directory.Exists(l)) languagesDirs.Add(l);
                }
                languagesDirs = languagesDirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (languagesDirs.Count == 0)
                {
                    logger.Warn($"[{mod.Name}] 无 Languages 目录，DefInjected 跳过");
                    return result;
                }
                outputBase = languagesDirs[0];
            }
            else
            {
                outputBase = options.Mode == OutputMode.SkeletonMod
                    ? Path.Combine(options.CustomDir, "Languages")
                    : Path.Combine(options.CustomDir, "Languages");
                foreach (var dir in mod.GetContentDirs(options.GameVersion))
                {
                    string l = Path.Combine(dir, "Languages");
                    if (Directory.Exists(l)) languagesDirs.Add(l);
                }
                Directory.CreateDirectory(Path.Combine(outputBase, options.LanguageFolderResolved, "DefInjected"));
            }
            string defInjectedDir = Path.Combine(outputBase, options.LanguageFolderResolved, "DefInjected");

            string? defLinked = Path.Combine(outputBase, options.LanguageFolderResolved, "DefLinked");
            if (Directory.Exists(defLinked) && !Directory.Exists(defInjectedDir))
            {
                Directory.Move(defLinked, defInjectedDir);
                System.Threading.Thread.Sleep(200);
            }
            if (!Directory.Exists(defInjectedDir)) Directory.CreateDirectory(defInjectedDir);
            foreach (var f in new DirectoryInfo(defInjectedDir).GetFiles("*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    if (options.Mode != OutputMode.SkeletonMod) XmlIO.BackupFile(f.FullName);
                    f.Delete();
                }
                catch (Exception ex) { logger.Error($"无法删除 {f.Name}: {ex.Message}"); }
            }

            var byType = defs.GroupBy(d => d.DefTypeName, StringComparer.OrdinalIgnoreCase);
            foreach (var typeGroup in byType)
            {
                try
                {
                    GenerateForType(typeGroup.Key, typeGroup.ToList(), defInjectedDir, existingInjections, result);
                }
                catch (Exception ex)
                {
                    logger.Error($"DefInjected 类型 {typeGroup.Key} 处理失败: {ex.Message}");
                }
            }
            return result;
        }

        private static void GenerateForType(
            string defTypeName,
            List<OfflineDef> defs,
            string defInjectedDir,
            Dictionary<string, string> existing,
            ExtractResult result)
        {
            var byFile = defs.GroupBy(d => d.FileName, StringComparer.OrdinalIgnoreCase);
            foreach (var fileGroup in byFile)
            {
                string fileName = fileGroup.Key;
                var doc = new XDocument();
                var rootEl = new XElement("LanguageData");
                doc.Add(rootEl);
                rootEl.Add(new XComment("NEWLINE"));
                bool any = false;

                foreach (var def in fileGroup.OrderBy(d => d.DefName, StringComparer.Ordinal))
                {
                    bool defHeader = false;
                    var injections = DefInjectionWalker.Walk(def);
                    var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var inj in injections)
                    {
                        if (inj.IsCollection)
                        {
                            var list = inj.CollectionValues!;
                            bool hasIndexedTranslation = Enumerable.Range(0, list.Count).Any(i => existing.ContainsKey(inj.NormalizedPath + "." + i));
                            if (hasIndexedTranslation)
                            {
                                for (int i = 0; i < list.Count; i++)
                                {
                                    string nPath = inj.NormalizedPath + "." + i;
                                    string sPath = inj.SuggestedPath + "." + i;
                                    string en = list[i];
                                    if (!DefInjectionWalker.ShouldCheckMissing(en) && !existing.ContainsKey(nPath)) continue;
                                    if (!defHeader) { defHeader = true; }
                                    any = true;
                                    if (en.Length > 0)
                                        rootEl.Add(new XComment(XmlIO.SanitizeXComment(" EN: " + en.Replace("\n", "\\n") + " ")));
                                    rootEl.Add(MakeNode(sPath, existing.TryGetValue(nPath, out var v) ? v : null));
                                    result.KeysTotal++;
                                    if (existing.ContainsKey(nPath)) result.KeysBackfilled++; else result.KeysTodo++;
                                    usedPaths.Add(nPath);
                                }
                            }
                            else
                            {
                                bool anyMeaningful = list.Any(v => DefInjectionWalker.ShouldCheckMissing(v));
                                if (!anyMeaningful && !existing.ContainsKey(inj.NormalizedPath)) continue;
                                defHeader = true;
                                any = true;
                                string enComment = ListToLiComment(list);
                                if (enComment.Length > 0)
                                    rootEl.Add(new XComment(XmlIO.SanitizeXComment(" EN:\n" + enComment + "\n  ")));
                                rootEl.Add(MakeListNode(inj.SuggestedPath, existing.TryGetValue(inj.NormalizedPath, out var fv) ? fv : null, list.Count));
                                result.KeysTotal++;
                                if (existing.ContainsKey(inj.NormalizedPath)) result.KeysBackfilled++; else result.KeysTodo++;
                                usedPaths.Add(inj.NormalizedPath);
                            }
                        }
                        else
                        {
                            string en = inj.Value ?? "";
                            if (!DefInjectionWalker.ShouldCheckMissing(en) && !existing.ContainsKey(inj.NormalizedPath)) continue;
                            defHeader = true;
                            any = true;
                            if (en.Length > 0)
                                rootEl.Add(new XComment(XmlIO.SanitizeXComment(" EN: " + en.Replace("\n", "\\n") + " ")));
                            rootEl.Add(MakeNode(inj.SuggestedPath, existing.TryGetValue(inj.NormalizedPath, out var v) ? v : null));
                            result.KeysTotal++;
                            if (existing.ContainsKey(inj.NormalizedPath)) result.KeysBackfilled++; else result.KeysTodo++;
                            usedPaths.Add(inj.NormalizedPath);
                        }
                    }

                    var unused = existing
                        .Where(kv => kv.Key.StartsWith(def.DefName + ".", StringComparison.OrdinalIgnoreCase) && !usedPaths.Contains(kv.Key))
                        .ToList();
                    if (unused.Count > 0)
                    {
                        any = true;
                        rootEl.Add(new XComment(" UNUSED "));
                        foreach (var kv in unused)
                            rootEl.Add(MakeNode(kv.Key, kv.Value));
                        rootEl.Add(new XComment("NEWLINE"));
                        result.KeysUnused += unused.Count;
                    }

                    if (defHeader)
                        rootEl.Add(new XComment("NEWLINE"));
                }

                if (any)
                {
                    string typeDir = Path.Combine(defInjectedDir, defTypeName.Contains('.') ? defTypeName : defTypeName);
                    Directory.CreateDirectory(typeDir);
                    XmlIO.SaveWithProcessedNewlineTags(doc, Path.Combine(typeDir, EnsureXmlExt(fileName)));
                    result.FilesWritten++;
                }
            }
        }

        private static string EnsureXmlExt(string fileName) =>
            fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".xml";

        private static string ListToLiComment(List<string> list)
        {
            var sb = new StringBuilder();
            foreach (var item in list)
            {
                sb.Append("<li>");
                if (!string.IsNullOrEmpty(item)) sb.Append(item.Replace("\n", "\\n"));
                sb.Append("</li>");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        private static XElement MakeNode(string path, string? translation)
        {
            if (translation == null || translation == "TODO")
                return new XElement(path, new XText("TODO"));
            return new XElement(path, new XText(translation.Replace("\n", "\\n")));
        }

        private static XElement MakeListNode(string path, string? translation, int enCount)
        {
            var el = new XElement(path);
            if (translation == null)
            {
                el.Add(new XText("TODO"));
                return el;
            }
            var lines = translation.Split("\\n", StringSplitOptions.None);
            foreach (var line in lines)
                el.Add(new XElement("li", new XText(line)));
            return el;
        }
    }
}
