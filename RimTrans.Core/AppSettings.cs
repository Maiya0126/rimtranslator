using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RimTrans.Core
{
    public class AppSettings
    {
        public string WorkshopDir { get; set; } = @"";
        public List<string> ExtraWorkshopDirs { get; set; } = new();
        public string LocalModsDir { get; set; } = @"";
        public string GameDataDir { get; set; } = @"";
        public List<string> CustomSources { get; set; } = new();
        public string GameVersion { get; set; } = "1.6";
        public string TargetLanguage { get; set; } = "ChineseSimplified";
        public string TranslatorAuthor { get; set; } = "";
        public string AiProvider { get; set; } = "Player2";
        public string AiEndpoint { get; set; } = "http://127.0.0.1:4315/v1/chat/completions";
        public string AiApiKey { get; set; } = "";
        public string AiModel { get; set; } = "Default";

        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RimTranslator", "settings.json");

        /// <summary>旧目录名迁移：RimTrans → RimTranslator（老用户升级无感）。</summary>
        public static void MigrateLegacyDir()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string oldDir = Path.Combine(appData, "RimTrans");
                string newDir = Path.Combine(appData, "RimTranslator");
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
                {
                    Directory.Move(oldDir, newDir);
                }
                else if (Directory.Exists(oldDir))
                {
                    foreach (var f in Directory.EnumerateFiles(oldDir))
                    {
                        string dest = Path.Combine(newDir, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Copy(f, dest);
                    }
                }
            }
            catch { }
        }

        public static AppSettings Load()
        {
            AppSettings settings;
            try
            {
                settings = File.Exists(SettingsPath)
                    ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                    : new AppSettings();
            }
            catch { settings = new AppSettings(); }

            // 兼容旧配置：默认路径改为空，首次启动（或老配置指向不存在的目录）时自动探测
            if (string.IsNullOrEmpty(settings.WorkshopDir) || !Directory.Exists(settings.WorkshopDir))
            {
                var detected = SteamLocator.FindWorkshopDirs();
                if (detected.Count > 0)
                    settings.WorkshopDir = detected[0];
                foreach (var d in detected.Skip(1))
                    if (!settings.ExtraWorkshopDirs.Contains(d, StringComparer.OrdinalIgnoreCase))
                        settings.ExtraWorkshopDirs.Add(d);
            }
            if (string.IsNullOrEmpty(settings.LocalModsDir) || !Directory.Exists(settings.LocalModsDir))
            {
                string? local = GuessLocalMods(settings.WorkshopDir);
                if (local != null) settings.LocalModsDir = local;
            }
            if (string.IsNullOrEmpty(settings.GameDataDir) || !Directory.Exists(settings.GameDataDir))
            {
                string? data = GuessGameData(settings.WorkshopDir);
                if (data != null) settings.GameDataDir = data;
            }
            return settings;
        }

        /// <summary>从工坊目录反推游戏本体：.../workshop/content/294100 → 同库 common/RimWorld/{Mods,Data}。</summary>
        internal static string? GuessLocalMods(string? workshopDir)
        {
            if (string.IsNullOrEmpty(workshopDir)) return null;
            try
            {
                string common = Path.GetFullPath(Path.Combine(workshopDir, "..", "..", "..", "..", "common"));
                string mods = Path.Combine(common, "RimWorld", "Mods");
                return Directory.Exists(mods) ? mods : null;
            }
            catch { return null; }
        }

        internal static string? GuessGameData(string? workshopDir)
        {
            if (string.IsNullOrEmpty(workshopDir)) return null;
            try
            {
                string common = Path.GetFullPath(Path.Combine(workshopDir, "..", "..", "..", "..", "common"));
                string data = Path.Combine(common, "RimWorld", "Data");
                return Directory.Exists(data) ? data : null;
            }
            catch { return null; }
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
