using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimTrans.Core
{
    public static class GameLanguageFinder
    {
        public static List<string> GetLanguages(string dataDir)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "English" };
            foreach (var n in EnumerateCoreLanguageNames(dataDir))
            {
                int sp = n.IndexOf(' ');
                int pa = n.IndexOf('(');
                int cut = sp < 0 ? pa : pa < 0 ? sp : Math.Min(sp, pa);
                string baseName = cut > 0 ? n[..cut] : n;
                if (baseName.Length > 0) set.Add(baseName);
            }
            return set.ToList();
        }

        /// <summary>
        /// 解析目标语言的完整输出文件夹名：与游戏 Data\Core\Languages 的 tar 名精确一致
        /// （如 "ChineseSimplified (简体中文)"，ASCII 括号+空格）。
        /// 游戏按 folderName 加载、"(" 前缀作 legacyFolderName 合并同一语言，
        /// 因此完整名文件夹与原 Mod 的纯名文件夹并存时，本工具译文优先生效且互不覆盖。
        /// 找不到（无 Data 目录/English）返回原名。
        /// </summary>
        public static string ResolveOutputFolderName(string dataDir, string language)
        {
            foreach (var n in EnumerateCoreLanguageNames(dataDir))
            {
                if (n.Equals(language, StringComparison.OrdinalIgnoreCase)) return language;
                if (n.StartsWith(language + " ", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(language + " (", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith(language + "(", StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            return language;
        }

        private static IEnumerable<string> EnumerateCoreLanguageNames(string dataDir)
        {
            string core = Path.Combine(dataDir ?? "", "Core", "Languages");
            if (!Directory.Exists(core)) yield break;
            foreach (var entry in Directory.EnumerateFileSystemEntries(core))
            {
                string n = Path.GetFileName(entry);
                if (n.Equals("English", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
                n = n.Trim();
                if (n.Length > 0) yield return n;
            }
        }
    }
}
