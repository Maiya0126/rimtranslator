using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace RimTrans.Core
{
    /// <summary>Steam 库与 RimWorld 相关目录的自动探测。</summary>
    public static class SteamLocator
    {
        /// <summary>找到所有含 RimWorld 工坊内容（app 294100）的 Steam 库。</summary>
        public static List<string> FindWorkshopDirs()
        {
            var results = new List<string>();
            foreach (var root in FindLibraryRoots())
            {
                string ws = Path.Combine(root, "steamapps", "workshop", "content", "294100");
                if (Directory.Exists(ws)) results.Add(ws);
            }
            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>从注册表 Steam 安装路径 + 各库 libraryfolders.vdf 解析全部库根目录。</summary>
        private static List<string> FindLibraryRoots()
        {
            var roots = new List<string>();

            // 1) 注册表 Steam 主程序路径
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    string? steamExe = GetSteamPath();
                    if (!string.IsNullOrEmpty(steamExe))
                        roots.Add(Path.GetDirectoryName(steamExe!)!);
                }
                catch { }
            }

            // 2) 各库的 libraryfolders.vdf（可能互相引用，逐一读）
            foreach (var root in roots.ToList())
            {
                try
                {
                    string vdf = Path.Combine(root, "config", "libraryfolders.vdf");
                    if (!File.Exists(vdf)) continue;
                    foreach (var line in File.ReadLines(vdf))
                    {
                        int i = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                        if (i < 0) continue;
                        int s = line.IndexOf('"', i + 6);
                        int e = line.IndexOf('"', s + 1);
                        if (s < 0 || e <= s) continue;
                        string p = line.Substring(s + 1, e - s - 1)
                            .Replace(@"\\", @"\");
                        if (Directory.Exists(p) && !roots.Contains(p, StringComparer.OrdinalIgnoreCase))
                            roots.Add(p);
                    }
                }
                catch { }
            }
            return roots;
        }

        [SupportedOSPlatform("windows")]
        private static string? GetSteamPath()
        {
            if (!OperatingSystem.IsWindows()) return null;
            string? steamExe = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
            if (string.IsNullOrEmpty(steamExe))
                steamExe = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            return steamExe;
        }
    }
}
