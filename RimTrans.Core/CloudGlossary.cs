using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace RimTrans.Core
{
    /// <summary>
    /// 云端词库：从 GitHub 仓库拉取官方译名词库（defName→中文，按 DefType）。
    /// 仓库更新即可让所有用户词库热更新，无需发软件新版本。
    /// 缓存到 %APPDATA%\RimTranslator\cloud-glossary.csv，7 天自动刷新，也可手动刷新。
    /// </summary>
    public static class CloudGlossary
    {
        public const string RawUrl = "https://raw.githubusercontent.com/Maiya0126/rimtranslator/main/glossary/rimworld-zh-glossary.csv";
        public const string RepoPage = "https://github.com/Maiya0126/rimtranslator/tree/main/glossary";

        private static string CachePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimTranslator", "cloud-glossary.csv");

        public class Entry
        {
            public string DefType = "";
            public string DefName = "";
            public string Zh = "";
        }

        /// <summary>词库条目（DefName→中文）。匹配时既可按 defName 又可按英文 label 近似使用。</summary>
        public static Dictionary<string, string> Map { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public static bool Loaded => Map.Count > 0;
        public static DateTime CachedAt { get; private set; }

        public static async Task<(int count, string message)> FetchAsync(bool force = false)
        {
            if (!force && Loaded) return (Map.Count, "已加载");

            bool needDownload = true;
            if (!force && File.Exists(CachePath))
            {
                var fi = new FileInfo(CachePath);
                if ((DateTime.Now - fi.LastWriteTime).TotalDays < 7)
                {
                    LoadFromFile(CachePath);
                    needDownload = false;
                }
            }

            if (needDownload)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                string csv = await http.GetStringAsync(RawUrl);
                string tmp = CachePath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                await File.WriteAllTextAsync(tmp, csv);
                if (File.Exists(CachePath)) File.Delete(CachePath);
                File.Move(tmp, CachePath);
                LoadFromFile(CachePath);
            }
            return (Map.Count, needDownload ? "已从云端拉取" : "使用本地缓存");
        }

        private static void LoadFromFile(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                // CSV: "DefType","DefName","Zh"
                var parts = line.Split("\",\"");
                if (parts.Length != 3) continue;
                string defName = parts[1].Trim('"', ' ');
                string zh = parts[2].Trim('"', ' ');
                if (defName.Length > 0 && zh.Length > 0)
                    map[defName] = zh;
            }
            Map = map;
            CachedAt = File.GetLastWriteTime(path);
        }

        public static bool TryMatch(string key, out string zh) => Map.TryGetValue(key ?? "", out zh!);
    }
}
