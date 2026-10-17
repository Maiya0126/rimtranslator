using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RimTrans.Core.Extraction
{
    /// <summary>
    /// 生成独立汉化 Mod 骨架（About.xml / LoadFolders.xml / Languages）。
    /// </summary>
    public static class SkeletonModBuilder
    {
        public static string Build(string targetDir, ModInfo sourceMod, ExtractionOptions options, string translatorAuthor = "")
        {
            string pkgId = string.IsNullOrWhiteSpace(sourceMod.PackageId)
                ? "Unknown.zh"
                : sourceMod.PackageId.Trim() + ".zh";
            string name = sourceMod.Name + " - " + options.Language + " Translation";
            string author = string.IsNullOrWhiteSpace(translatorAuthor)
                ? "RimTranslator"
                : translatorAuthor.Trim();

            Directory.CreateDirectory(Path.Combine(targetDir, "About"));
            Directory.CreateDirectory(Path.Combine(targetDir, "Languages", options.Language));

            // 描述：保留原 Mod 描述 + 署名原作者与本翻译
            string descBody = string.IsNullOrWhiteSpace(sourceMod.Description)
                ? ""
                : sourceMod.Description.Trim() + "\n\n";
            string origAuthor = string.IsNullOrWhiteSpace(sourceMod.Author) ? "" : sourceMod.Author.Trim();
            string desc = descBody
                + (origAuthor.Length > 0
                    ? $"Original mod: {origAuthor}\n"
                    : "")
                + $"{FriendlyLanguage(options.Language)} translation by {author}, generated with {ExtractionOptions.GeneratorName}.";

            string aboutPath = Path.Combine(targetDir, "About", "About.xml");
            string aboutXml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<ModMetaData>\n" +
                $"  <name>{Escape(name)}</name>\n" +
                $"  <author>{Escape(author)}</author>\n" +
                $"  <packageId>{Escape(pkgId)}</packageId>\n" +
                "  <supportedVersions>\n" +
                $"    <li>{options.GameVersion}</li>\n" +
                "  </supportedVersions>\n" +
                $"  <description>{Escape(desc)}</description>\n" +
                (string.IsNullOrWhiteSpace(sourceMod.PackageId)
                    ? ""
                    : "  <loadAfter>\n    <li>" + Escape(sourceMod.PackageId.Trim()) + "</li>\n  </loadAfter>\n") +
                "  <modDependencies>\n    <li>\n" +
                $"      <packageId>{Escape(sourceMod.PackageId.Trim())}</packageId>\n" +
                $"      <displayName>{Escape(sourceMod.Name)}</displayName>\n" +
                (sourceMod.Source == Core.ModSource.Workshop && !string.IsNullOrEmpty(sourceMod.PublishedFileId)
                    ? "      <steamWorkshopUrl>steam://url/CommunityFilePage/" + sourceMod.PublishedFileId + "</steamWorkshopUrl>\n"
                    : "") +
                "    </li>\n  </modDependencies>\n" +
                "</ModMetaData>\n";
            File.WriteAllText(aboutPath, aboutXml, new UTF8Encoding(true));

            string loadFoldersPath = Path.Combine(targetDir, "LoadFolders.xml");
            File.WriteAllText(loadFoldersPath,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<loadFolders>\n</loadFolders>\n",
                new UTF8Encoding(true));

            return targetDir;
        }

        private static string Escape(string s) => s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

        private static string FriendlyLanguage(string folder) => folder switch
        {
            "ChineseSimplified" => "Simplified Chinese",
            "ChineseTraditional" => "Traditional Chinese",
            "Japanese" => "Japanese",
            "Korean" => "Korean",
            "Russian" => "Russian",
            "French" => "French",
            "German" => "German",
            "Spanish" or "SpanishLatin" => "Spanish",
            "Portuguese" or "PortugueseBrazilian" => "Portuguese",
            _ => folder,
        };
    }
}
