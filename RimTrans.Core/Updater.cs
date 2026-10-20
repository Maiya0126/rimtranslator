using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RimTrans.Core
{
    /// <summary>
    /// 应用内自动更新：查询 GitHub Releases → 下载（full 自包含 exe）→ 启动 helper 延时自替换。
    /// 更新器命名 RimTranslator.update.exe，等待主进程退出后替换主程序并重启。
    /// </summary>
    public static class Updater
    {
        public const string CurrentVersion = "1.3.7";
        private const string ReleaseApi = "https://api.github.com/repos/Maiya0126/rimtranslator/releases/latest";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public class UpdateInfo
        {
            public string Tag = "";
            public string Notes = "";
            public string ExeUrl = "";
            public long Size;
            public string HtmlUrl = "";
        }

        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
                req.Headers.Add("User-Agent", "RimTranslator");
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(tag)) return null;
                var info = new UpdateInfo
                {
                    Tag = tag,
                    Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    HtmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "",
                };
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (name.EndsWith("-full.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            info.ExeUrl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() ?? "" : "";
                            info.Size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            break;
                        }
                    }
                }
                return info;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsNewer(string tag, string current)
        {
            static int[] Ver(string s)
            {
                var parts = s.TrimStart('v', 'V').Split('.');
                var nums = new int[4];
                for (int i = 0; i < 4; i++)
                    nums[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
                return nums;
            }
            var a = Ver(tag);
            var b = Ver(current);
            for (int i = 0; i < 4; i++)
            {
                if (a[i] != b[i]) return a[i] > b[i];
            }
            return false;
        }

        /// <summary>下载到临时文件，返回路径。progress: (已下载字节, 总字节)。</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<(long done, long total)>? progress, byte[]? expectedSha256 = null)
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"RimTranslator-{info.Tag}-update.exe");
            using var resp = await Http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.Size;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tmp);
            var buf = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read));
                downloaded += read;
                progress?.Report((downloaded, total));
            }
            return tmp;
        }

        /// <summary>
        /// 启动更新 helper：写一个 .update.cmd 到临时目录，等待主进程退出后替换 exe 并重启。
        /// 调用后应立即关闭本程序全部窗口并 Environment.Exit。
        /// </summary>
        public static void LaunchSelfReplace(string downloadedExe, int exitPid)
        {
            string currentExe = Environment.ProcessPath ?? "";
            string target = Path.ChangeExtension(currentExe, ".new.exe");
            File.Copy(downloadedExe, target, overwrite: true);
            string cmd = Path.Combine(Path.GetTempPath(), $"rimtranslator-update-{exitPid}.cmd");
            string script =
                "@echo off\r\n" +
                $"title RimTranslator Updater\r\n" +
                $":wait\r\n" +
                $"tasklist /FI \"PID eq {exitPid}\" | find /I \"{Path.GetFileName(currentExe)}\" >nul\r\n" +
                $"if not errorlevel 1 (\r\n" +
                $"  timeout /t 1 /nobreak >nul\r\n" +
                $"  goto wait\r\n" +
                $")\r\n" +
                $"move /y \"{target}\" \"{currentExe}\"\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                $"del \"%~f0\"\r\n";
            File.WriteAllText(cmd, script, System.Text.Encoding.ASCII);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
            {
                CreateNoWindow = false,
                UseShellExecute = false,
            });
        }
    }
}
