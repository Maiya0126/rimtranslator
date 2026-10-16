using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimTrans.Core.Extraction
{
    public class KeyedReplacement
    {
        public string Key = "";
        public string Value = "";
        public bool IsPlaceholder;
        public string FileSourceFullPath = "";
    }

    /// <summary>
    /// 离线版 ExtractKey：以 Mod 的 Languages\English\Keyed 为源，生成目标语言 Keyed 文件。
    /// 行为对齐参考模组 Translate_Library.Window_ExtractTranslation.ExtractSingleKey。
    /// </summary>
    public static class KeyedExtractor
    {
        public static ExtractResult Extract(
            ModInfo mod,
            ExtractionOptions options,
            List<KeyedReplacement>? existingTranslations,
            IExtractLogger logger)
        {
            var result = new ExtractResult();
            var translations = existingTranslations ?? new List<KeyedReplacement>();

            var languagesDirs = FindLanguagesDirs(mod, options, out var outputRoot);
            if (languagesDirs.Count == 0)
            {
                logger.Warn($"[{mod.Name}] 未找到 Languages 目录，跳过 Keyed 提取");
                return result;
            }

            var writtenUnused = new HashSet<KeyedReplacement>();
            foreach (var langRoot in languagesDirs)
            {
                string languageFolderPath = Path.Combine(outputRoot(langRoot), options.Language);
                ExtractSingle(mod, options, translations, languageFolderPath, Path.GetDirectoryName(languageFolderPath)!, writtenUnused, result, logger);
            }
            return result;
        }

        private static List<string> FindLanguagesDirs(ModInfo mod, ExtractionOptions options, out Func<string, string> outputRoot)
        {
            var dirs = new List<string>();
            foreach (var contentDir in mod.GetContentDirs(options.GameVersion))
            {
                string languages = Path.Combine(contentDir, "Languages");
                if (Directory.Exists(languages))
                    dirs.Add(languages);
            }
            dirs = dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            outputRoot = languagesDir =>
            {
                switch (options.Mode)
                {
                    case OutputMode.CustomDir:
                        return Path.Combine(options.CustomDir, options.Language, "..");
                    case OutputMode.SkeletonMod:
                        return Path.Combine(options.CustomDir, "Languages", options.Language, "..");
                    default:
                        return languagesDir;
                }
            };

            if (options.Mode == OutputMode.CustomDir || options.Mode == OutputMode.SkeletonMod)
            {
                string baseDir = options.Mode == OutputMode.SkeletonMod
                    ? Path.Combine(options.CustomDir, "Languages")
                    : options.CustomDir;
                outputRoot = _ => baseDir;
                return dirs;
            }
            return dirs;
        }

        private static void ExtractSingle(
            ModInfo mod,
            ExtractionOptions options,
            List<KeyedReplacement> translations,
            string languageFolderPath,
            string languagesRootForEnglish,
            HashSet<KeyedReplacement> writtenUnused,
            ExtractResult result,
            IExtractLogger logger)
        {
            string codeLinked = Path.Combine(languageFolderPath, "CodeLinked");
            string keyedDir = Path.Combine(languageFolderPath, "Keyed");
            if (Directory.Exists(codeLinked) && !Directory.Exists(keyedDir))
            {
                Directory.Move(codeLinked, keyedDir);
                System.Threading.Thread.Sleep(200);
            }

            string englishKeyed = Path.Combine(languagesRootForEnglish, "English", "Keyed");
            if (!Directory.Exists(englishKeyed))
            {
                logger.Info($"[{mod.Name}] 跳过：English Keyed 不存在（{englishKeyed}）");
                return;
            }
            var englishFiles = new DirectoryInfo(englishKeyed).GetFiles("*.xml", SearchOption.AllDirectories);
            if (englishFiles.Length == 0)
            {
                logger.Info($"[{mod.Name}] 跳过：English Keyed 无 XML 文件（{englishKeyed}）");
                return;
            }

            if (!Directory.Exists(keyedDir)) Directory.CreateDirectory(keyedDir);
            foreach (var f in new DirectoryInfo(keyedDir).GetFiles("*.xml", SearchOption.AllDirectories))
            {
                try { XmlIO.BackupFile(f.FullName); f.Delete(); } catch (Exception ex) { logger.Error($"无法删除 {f.Name}: {ex.Message}"); }
            }

            foreach (var f in englishFiles)
            {
                try
                {
                    string rel = Path.GetRelativePath(englishKeyed, f.FullName);
                    string target = Path.Combine(keyedDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    f.CopyTo(target, true);
                }
                catch (Exception ex)
                {
                    logger.Error($"无法复制 {f.Name}: {ex.Message}");
                }
            }

            var targetFiles = new DirectoryInfo(keyedDir).GetFiles("*.xml", SearchOption.AllDirectories);
            foreach (var file in targetFiles)
            {
                try
                {
                    ProcessOneFile(file, translations, writtenUnused, result, logger);
                }
                catch (Exception ex)
                {
                    logger.Error($"无法处理 {file.Name}: {ex.Message}");
                }
            }
        }

        private static void ProcessOneFile(
            FileInfo file,
            List<KeyedReplacement> translations,
            HashSet<KeyedReplacement> writtenUnused,
            ExtractResult result,
            IExtractLogger logger)
        {
            var doc = XDocument.Load(file.FullName, LoadOptions.PreserveWhitespace);
            var root = doc.DescendantNodes().OfType<XElement>().FirstOrDefault();
            if (root == null) return;
            result.FilesWritten++;

            foreach (var el in root.Elements().ToList())
            {
                var textNode = el.Nodes().OfType<XText>().FirstOrDefault();
                string en = textNode?.Value ?? "";

                foreach (var tr in translations.Where(t => t.Key == el.Name.ToString()))
                    writtenUnused.Add(tr);

                foreach (var node in el.Nodes().ToList())
                {
                    if (node is XText t && !string.IsNullOrWhiteSpace(t.Value))
                    {
                        node.AddBeforeSelf(new XComment(XmlIO.SanitizeXComment(" EN: " + t.Value + " ")));
                        node.AddBeforeSelf(new XText(Environment.NewLine));
                        node.AddBeforeSelf(new XText("  "));
                    }
                    node.Remove();
                }

                var hit = translations.FirstOrDefault(t => t.Key == el.Name.ToString() && !t.IsPlaceholder && !string.IsNullOrEmpty(t.Value));
                if (hit != null)
                {
                    el.Add(new XText(hit.Value.Replace("\n", "\\n")));
                    result.KeysBackfilled++;
                }
                else
                {
                    el.Add(new XText("TODO"));
                    result.KeysTodo++;
                }
                result.KeysTotal++;
            }

            bool unusedHeader = false;
            foreach (var tr in translations)
            {
                if (writtenUnused.Contains(tr)) continue;
                if (!string.Equals(tr.FileSourceFullPath, file.FullName, StringComparison.OrdinalIgnoreCase)) continue;

                if (!unusedHeader)
                {
                    root.Add(new XText("  "));
                    root.Add(new XComment(" UNUSED "));
                    root.Add(new XText(Environment.NewLine));
                    unusedHeader = true;
                }
                var el = new XElement(tr.Key);
                if (tr.IsPlaceholder)
                    el.Add(new XText("TODO"));
                else if (!string.IsNullOrEmpty(tr.Value))
                    el.Add(new XText(tr.Value.Replace("\n", "\\n")));
                root.Add(new XText("  "));
                root.Add(el);
                root.Add(new XText(Environment.NewLine));
                writtenUnused.Add(tr);
                result.KeysUnused++;
            }
            if (unusedHeader)
                root.Add(new XText(Environment.NewLine));

            XmlIO.SaveWithProcessedNewlineTags(doc.Root!, file.FullName);
        }
    }
}
