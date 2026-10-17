using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RimTrans.Core
{
    public class AppSettings
    {
        public string WorkshopDir { get; set; } = @"D:\Steam\steamapps\workshop\content\294100";
        public string LocalModsDir { get; set; } = @"D:\Games\steamapps\common\RimWorld\Mods";
        public string GameDataDir { get; set; } = @"D:\Games\steamapps\common\RimWorld\Data";
        public List<string> CustomSources { get; set; } = new();
        public string GameVersion { get; set; } = "1.6";
        public string TargetLanguage { get; set; } = "ChineseSimplified";
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
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
