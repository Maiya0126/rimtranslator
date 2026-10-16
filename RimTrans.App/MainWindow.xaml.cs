using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RimTrans.Core;
using RimTrans.Core.Defs;
using RimTrans.Core.Extraction;

namespace RimTrans.App
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly ModScanner _scanner = new();
        private List<ModInfo> _allMods = new();
        private bool _versionComboUpdating;

        public MainWindow()
        {
            InitializeComponent();
            _uiReady = true;
            LoadUiFromSettings();
            Log("边缘译者 RimTranslator 已启动。点击「刷新列表」扫描 Mod。");
            _ = WarmCloudGlossaryAsync();
        }

        private async Task WarmCloudGlossaryAsync()
        {
            try
            {
                var (count, msg) = await Task.Run(() => CloudGlossary.FetchAsync());
                if (count > 0)
                    Log($"☁️ 云端词库{msg}：{count} 条（defName→中文，翻译时自动生效）");
            }
            catch
            {
                Log("☁️ 云端词库未拉取（无网或首次），翻译时会自动重试。");
            }
        }

        internal static readonly Dictionary<string, string> LanguageNames = new()
        {
            ["ChineseSimplified"] = "简体中文",
            ["ChineseTraditional"] = "繁體中文",
            ["English"] = "English（英语）",
            ["Japanese"] = "日本語（日语）",
            ["Korean"] = "한국어（韩语）",
            ["Russian"] = "Русский（俄语）",
            ["French"] = "Français（法语）",
            ["German"] = "Deutsch（德语）",
            ["Spanish"] = "Español（西班牙语）",
            ["SpanishLatin"] = "Español Latino（拉美西语）",
            ["Portuguese"] = "Português（葡萄牙语）",
            ["PortugueseBrazilian"] = "Português BR（巴西葡语）",
            ["Italian"] = "Italiano（意大利语）",
            ["Polish"] = "Polski（波兰语）",
            ["Czech"] = "Čeština（捷克语）",
            ["Catalan"] = "Català（加泰罗尼亚语）",
            ["Danish"] = "Dansk（丹麦语）",
            ["Dutch"] = "Nederlands（荷兰语）",
            ["Estonian"] = "Eesti（爱沙尼亚语）",
            ["Finnish"] = "Suomi（芬兰语）",
            ["Greek"] = "Ελληνικά（希腊语）",
            ["Hungarian"] = "Magyar（匈牙利语）",
            ["Norwegian"] = "Norsk（挪威语）",
            ["Romanian"] = "Română（罗马尼亚语）",
            ["Slovak"] = "Slovenčina（斯洛伐克语）",
            ["Swedish"] = "Svenska（瑞典语）",
            ["Turkish"] = "Türkçe（土耳其语）",
            ["Ukrainian"] = "Українська（乌克兰语）",
            ["Vietnamese"] = "Tiếng Việt（越南语）",
        };

        public static string LanguageDisplayName(string folder) =>
            LanguageNames.TryGetValue(folder, out var name) ? name : folder;

        private void LoadUiFromSettings()
        {
            var langs = GameLanguageFinder.GetLanguages(_settings.GameDataDir);
            // 常用度排序：简中/繁中置顶，其后按游戏圈常用语言，其余字母序
            var order = new[] { "ChineseSimplified", "ChineseTraditional", "English", "Japanese", "Korean", "Russian", "French", "German", "Spanish", "PortugueseBrazilian", "Portuguese", "Italian", "Polish", "Czech", "Ukrainian", "Turkish", "Vietnamese" };
            var sorted = langs
                .OrderBy(l => { int i = Array.IndexOf(order, l); return i < 0 ? 999 : i; })
                .ThenBy(LanguageDisplayName)
                .ToList();
            CmbLanguage.ItemsSource = sorted.Select(l => new LanguageItem { Folder = l, Display = LanguageDisplayName(l) }).ToList();
            CmbLanguage.SelectedIndex = Math.Max(0, sorted.IndexOf(_settings.TargetLanguage));

            var versions = new List<string> { "1.4", "1.5", "1.6", "1.7" };
            CmbVersion.ItemsSource = versions;
            CmbVersion.SelectedItem = versions.Contains(_settings.GameVersion) ? _settings.GameVersion : "1.6";
        }

        public class LanguageItem
        {
            public string Folder { get; set; } = "";
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }

        private IEnumerable<(ModSource, string)> GetSelectedSources()
        {
            if (ChkWorkshop.IsChecked == true && Directory.Exists(_settings.WorkshopDir))
                yield return (ModSource.Workshop, _settings.WorkshopDir);
            if (ChkLocal.IsChecked == true && Directory.Exists(_settings.LocalModsDir))
                yield return (ModSource.Local, _settings.LocalModsDir);
            if (ChkData.IsChecked == true && Directory.Exists(_settings.GameDataDir))
                yield return (ModSource.GameData, _settings.GameDataDir);
            if (ChkCustom.IsChecked == true)
                foreach (var dir in _settings.CustomSources)
                    if (Directory.Exists(dir))
                        yield return (ModSource.Custom, dir);
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false;
            try
            {
                var sources = GetSelectedSources().ToList();
                Log($"开始扫描 {sources.Count} 个来源…");
                SetProgress("正在扫描…", 0);
                var mods = await Task.Run(() =>
                {
                    var sc = new ModScanner();
                    sc.ScanAll(sources);
                    return sc;
                });
                _scanner.Mods.Clear();
                foreach (var m in mods.Mods) _scanner.Mods.Add(m);
                _allMods = mods.Mods.OrderByDescending(m => m.AboutParsed)
                    .ThenBy(m => m.Source == ModSource.Workshop ? m.PublishedFileId : m.Name)
                    .ThenBy(m => m.Name)
                    .ToList();
                ApplyFilter();                Log($"扫描完成：目录 {mods.ScannedDirs} 个，成功 {mods.ParsedOk}，失败(无About) {mods.ParsedFail}，用时 {mods.ScanSeconds:F1}s");
                foreach (var g in _allMods.GroupBy(m => m.Source).OrderBy(g => g.Key))
                    Log($"  {g.Key}: {g.Count()} 个");
                SetProgress("就绪", 0);
            }
            finally
            {
                BtnScan.IsEnabled = true;
            }
        }

        private static readonly string[] TranslationModKeywords =
        {
            "汉化", "漢化", "簡體", "简体", "繁體", "繁体", "中文", "简繁", "语言包",
            "日化", "韩化", "韓化", "chinese", "translation", "translate",
        };

        private static bool LooksLikeTranslationMod(ModInfo m)
        {
            string pkg = m.PackageId.ToLowerInvariant();
            // 包ID：ZH 前缀（社区翻译包惯例）/ 各语言后缀
            if (pkg.StartsWith("zh.") || pkg.StartsWith("zh-") || pkg.StartsWith("zh_") ||
                pkg.EndsWith(".zh") || pkg.EndsWith(".zh-cn") || pkg.EndsWith(".cn") || pkg.EndsWith(".ja") ||
                pkg.EndsWith(".ko") || pkg.EndsWith(".ru") || pkg.EndsWith(".chs") ||
                pkg.EndsWith("-zh") || pkg.EndsWith("_zh") || pkg.EndsWith("-chinese") || pkg.EndsWith("_chinese") ||
                pkg.EndsWith(".zh.hc") || pkg.EndsWith(".translation") || pkg.Contains("chinesepack") ||
                pkg.Contains("chinesetranslation") || pkg.Contains("chtrans") || pkg.EndsWith(".moujia.cn"))
                return true;
            // 名称：含 _zh / -zh / 汉化 字样，或以翻译关键词开头/结尾
            string name = m.Name.Trim();
            if (name.Contains("_zh", StringComparison.OrdinalIgnoreCase)
                || name.Contains("-zh", StringComparison.OrdinalIgnoreCase)
                || name.Contains("汉化") || name.Contains("漢化")
                || name.Contains("简繁", StringComparison.OrdinalIgnoreCase)
                || name.Contains("簡繁", StringComparison.OrdinalIgnoreCase)
                || name.Contains("中文", StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var kw in TranslationModKeywords)
            {
                if (name.StartsWith(kw, StringComparison.OrdinalIgnoreCase) || name.EndsWith(kw, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private bool _uiReady;

        private void ApplyFilter()
        {
            if (!_uiReady || TxtSearch == null || GridMods == null || TxtCount == null) return;
            string q = TxtSearch.Text?.Trim() ?? "";
            IEnumerable<ModInfo> query = _allMods;
            if (ChkHideTranslation.IsChecked == true)
                query = query.Where(m => !LooksLikeTranslationMod(m));
            if (q.Length > 0)
                query = query.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || m.PackageId.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || m.PublishedFileId.Contains(q, StringComparison.OrdinalIgnoreCase));
            GridMods.ItemsSource = query.Select(m => new ModRow(m)).ToList();
            TxtCount.Text = $"共 {((ICollection<ModRow>)GridMods.ItemsSource).Count} 个（全库 {_allMods.Count}）";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ChkHideTranslation_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

        private void BtnAddSource_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "添加自定义扫描源目录" };
            if (dlg.ShowDialog(this) == true)
            {
                if (!_settings.CustomSources.Contains(dlg.FolderName))
                {
                    _settings.CustomSources.Add(dlg.FolderName);
                    _settings.Save();
                    Log($"已添加自定义扫描源：{dlg.FolderName}");
                }
            }
        }

        private void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_versionComboUpdating || CmbVersion.SelectedItem is not string v) return;
            _versionComboUpdating = true;
            _settings.GameVersion = v;
            _settings.Save();
            _versionComboUpdating = false;
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguage.SelectedItem is not LanguageItem lang) return;
            if (_settings.TargetLanguage != lang.Folder)
            {
                _settings.TargetLanguage = lang.Folder;
                _settings.Save();
            }
        }

        private void BtnBrowseOut_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择输出目录" };
            if (dlg.ShowDialog(this) == true) TxtOutDir.Text = dlg.FolderName;
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (GridMods.SelectedItem is ModRow row)
                try { System.Diagnostics.Process.Start("explorer.exe", row.Mod.RootDir); } catch { }
        }

        private async void MenuTranslate_Click(object sender, RoutedEventArgs e)
        {
            await RunTranslateFlow(useSkeleton: false);
        }

        private async void MenuTranslateSkeleton_Click(object sender, RoutedEventArgs e)
        {
            await RunTranslateFlow(useSkeleton: true);
        }

        private async Task RunTranslateFlow(bool useSkeleton)
        {
            var selected = GridMods.SelectedItems.Cast<ModRow>().Select(r => r.Mod).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先在列表中选择至少一个 Mod。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string outDir = TxtOutDir.Text;
            if (useSkeleton && (string.IsNullOrWhiteSpace(outDir) || !Directory.Exists(outDir)))
            {
                MessageBox.Show(this, "「翻译并生成独立汉化Mod」需要先在下方选择输出目录（新 Mod 存放位置）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var aiSettings = new RimTrans.Core.AI.AiSettings
            {
                Provider = _settings.AiProvider,
                Endpoint = _settings.AiEndpoint,
                ApiKey = _settings.AiApiKey,
                Model = _settings.AiModel,
            };
            var aiClient = new RimTrans.Core.AI.AiClient(aiSettings);

            var options = new ExtractionOptions
            {
                GameVersion = _settings.GameVersion,
                Language = _settings.TargetLanguage,
                GameDataDir = _settings.GameDataDir,
                Mode = useSkeleton ? OutputMode.SkeletonMod : OutputMode.ModDir,
                CustomDir = outDir,
            };

            var logger = new UiLogger(this);
            BtnExtract.IsEnabled = false;
            try
            {
                // 术语库：自定义词库 + 官方库
                Log("构建术语库（自定义 + 官方）…");
                SetProgress("构建术语库…", 2);
                var glossary = new Glossary();
                glossary.AddRange(Glossary.LoadCustom(_settings.TargetLanguage));
                int customCount = glossary.Count;
                var official = await Task.Run(() => OfficialGlossaryBuilder.Build(_settings.GameDataDir, _settings.TargetLanguage));
                glossary.AddRange(official);
                Log($"术语库就绪：自定义 {customCount} 条，官方 {official.Count} 条");

                foreach (var mod in selected)
                {
                    Log($"── 翻译 [{mod.SourceDisplay}] {mod.Name}");
                    if (useSkeleton)
                    {
                        string skelDir = Path.Combine(outDir, SafeDirName(mod.Name));
                        SkeletonModBuilder.Build(skelDir, mod, options);
                        Log($"  已生成独立汉化Mod骨架：{skelDir}");
                    }

                    // 1. 提取（Keyed + DefInjected）
                    SetProgress($"提取：{mod.Name}", 5);
                    var keyedResult = await Task.Run(() => KeyedExtractor.Extract(mod, options, LoadExisting(mod, options), logger));
                    var defDirs = mod.GetContentDirs(options.GameVersion);
                    var patchDirs = ExtractContext.GetPatchDirs(mod, options.GameVersion);
                    if (patchDirs.Count > 0) Log($"  发现 {patchDirs.Count} 个 Patches 目录，应用补丁…");
                    var depDirs = ExtractContext.GetDependencyDefDirs(mod, _allMods.Select(r => r), options.GameVersion, msg => Log("  " + msg));
                    var defs = await Task.Run(() => XmlInheritanceMerger.LoadAndMerge(defDirs, mod.PackageId, msg => logger.Warn(msg),
                        extraDefDirs: depDirs.Count > 0 ? depDirs : null, patchDirs: patchDirs.Count > 0 ? patchDirs : null));
                    var defExisting = await Task.Run(() =>
                    {
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var dir in defDirs)
                        {
                            string languages = Path.Combine(dir, "Languages");
                            if (Directory.Exists(languages))
                                foreach (var kv in ExistingDefInjectionLoader.Load(languages, options.Language))
                                    dict[kv.Key] = kv.Value;
                        }
                        return dict;
                    });
                    var defResult = await Task.Run(() => DefInjectedGenerator.Generate(mod, options, defs, defExisting, logger));
                    Log($"  提取完成：Keyed {keyedResult.KeysTodo} 条 TODO，DefInjected {defResult.KeysTodo} 条 TODO");

                    // 2. 扫描 TODO → 术语回填 + AI
                    string languagesRoot = useSkeleton
                        ? Path.Combine(outDir, SafeDirName(mod.Name), "Languages")
                        : Path.Combine(mod.RootDir, "Languages");
                    var items = await Task.Run(() => RimTrans.Core.AI.TodoScanner.Scan(languagesRoot, options.Language));
                    if (items.Count == 0)
                    {
                        Log("  没有 TODO 条目，无需翻译。");
                        continue;
                    }
                    Log($"  待翻译 {items.Count} 条，开始术语匹配与 AI 翻译…");
                    var progress = new Progress<(int done, int total, string stage)>(p =>
                    {
                        SetProgress($"{p.stage} {p.done}/{p.total}", p.total == 0 ? 0 : p.done * 100.0 / p.total);
                    });
                    var translator = new RimTrans.Core.AI.BatchTranslator(aiClient, FriendlyLanguageName(options.Language));
                    await Task.Run(() => translator.TranslateAsync(items, glossary, progress));

                    int g = items.Count(i => i.Status == "glossary"), a = items.Count(i => i.Status == "ai"), f = items.Count(i => i.Status == "failed");
                    Log($"  翻译完成：术语 {g}，AI {a}，失败 {f}。打开审阅窗口…");
                    SetProgress("审阅中", 100);

                    var review = new ReviewWindow(items, msg => Log(msg)) { Owner = this };
                    review.ShowDialog();
                    Log($"  {mod.Name} 翻译流程结束。");
                }
                SetProgress("就绪（翻译流程完成）", 100);
            }
            catch (RimTrans.Core.AI.AiException ex)
            {
                Log($"[错误] {ex.Message}");
                MessageBox.Show(this, ex.Message, "翻译AI错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnExtract.IsEnabled = true;
            }
        }

        private static string FriendlyLanguageName(string folder) => folder switch
        {
            "ChineseSimplified" => "Simplified Chinese (简体中文)",
            "ChineseTraditional" => "Traditional Chinese (繁體中文)",
            "Japanese" => "Japanese (日本語)",
            "Korean" => "Korean (한국어)",
            "Russian" => "Russian (Русский)",
            "French" => "French (Français)",
            "German" => "German (Deutsch)",
            "Spanish" or "SpanishLatin" => "Spanish (Español)",
            "Portuguese" or "PortugueseBrazilian" => "Portuguese (Português)",
            _ => folder,
        };

        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridMods.SelectedItems.Cast<ModRow>().Select(r => r.Mod).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先在列表中选择至少一个 Mod（可右键打开菜单）。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var options = new ExtractionOptions
            {
                GameVersion = _settings.GameVersion,
                Language = _settings.TargetLanguage,
                GameDataDir = _settings.GameDataDir,
            };
            if (RbOutModDir.IsChecked == true) options.Mode = OutputMode.ModDir;
            else if (RbOutCustom.IsChecked == true)
            {
                if (!Directory.Exists(TxtOutDir.Text))
                {
                    MessageBox.Show(this, "请先点「浏览」选择自定义输出目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                options.Mode = OutputMode.CustomDir;
                options.CustomDir = TxtOutDir.Text;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TxtOutDir.Text) || !Directory.Exists(TxtOutDir.Text))
                {
                    MessageBox.Show(this, "请先点「浏览」选择独立汉化Mod的存放目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                options.Mode = OutputMode.SkeletonMod;
                options.CustomDir = TxtOutDir.Text;
            }

            if (options.Mode == OutputMode.ModDir)
            {
                bool toWorkshop = selected.Any(m => m.Source == ModSource.Workshop);
                var msg = toWorkshop
                    ? "将直接写入所选 Mod 原目录（含创意工坊目录）的 Languages 文件夹。\n覆盖前会自动生成 .bak 备份。是否继续？"
                    : "将写入所选 Mod 原目录的 Languages 文件夹。\n覆盖前会自动生成 .bak 备份。是否继续？";
                if (MessageBox.Show(this, msg, "覆盖警告", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                    return;
            }

            var logger = new UiLogger(this);
            BtnExtract.IsEnabled = false;
            try
            {
                int done = 0;
                foreach (var mod in selected)
                {
                    done++;
                    SetProgress($"提取 {done}/{selected.Count}：{mod.Name}", (done - 1) * 100.0 / selected.Count);
                    Log($"── 开始提取 [{mod.SourceDisplay}] {mod.Name}（目标语言 {options.Language}，版本 {options.GameVersion}）");
                    if (options.Mode == OutputMode.SkeletonMod)
                    {
                        string skelDir = Path.Combine(options.CustomDir, SafeDirName(mod.Name));
                        SkeletonModBuilder.Build(skelDir, mod, options);
                        Log($"  已生成独立汉化Mod骨架：{skelDir}");
                    }
                    var result = await Task.Run(() => KeyedExtractor.Extract(mod, options, LoadExisting(mod, options), logger));
                    Log($"  Keyed 完成：写入 {result.FilesWritten} 个文件，共 {result.KeysTotal} 键（回填 {result.KeysBackfilled}，TODO {result.KeysTodo}，UNUSED 归并 {result.KeysUnused}）");

                    var defDirs = mod.GetContentDirs(options.GameVersion);
                    var patchDirs2 = ExtractContext.GetPatchDirs(mod, options.GameVersion);
                    if (patchDirs2.Count > 0) Log($"  发现 {patchDirs2.Count} 个 Patches 目录，应用补丁…");
                    var depDirs2 = ExtractContext.GetDependencyDefDirs(mod, _allMods, options.GameVersion, msg => Log("  " + msg));
                    var defs = await Task.Run(() => XmlInheritanceMerger.LoadAndMerge(defDirs, mod.PackageId,
                        msg => logger.Warn(msg),
                        extraDefDirs: depDirs2.Count > 0 ? depDirs2 : null, patchDirs: patchDirs2.Count > 0 ? patchDirs2 : null));
                    Log($"  Def 解析：{defs.Count} 个（继承合并完成）");
                    var defExisting = await Task.Run(() =>
                    {
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var dir in defDirs)
                        {
                            string languages = Path.Combine(dir, "Languages");
                            if (Directory.Exists(languages))
                                foreach (var kv in ExistingDefInjectionLoader.Load(languages, options.Language))
                                    dict[kv.Key] = kv.Value;
                        }
                        return dict;
                    });
                    var defResult = await Task.Run(() => DefInjectedGenerator.Generate(mod, options, defs, defExisting, logger));
                    Log($"  DefInjected 完成：写入 {defResult.FilesWritten} 个文件，共 {defResult.KeysTotal} 条（回填 {defResult.KeysBackfilled}，TODO {defResult.KeysTodo}，UNUSED {defResult.KeysUnused}）");
                    Log($"  {mod.Name} 提取完成。");
                    SetProgress($"提取 {done}/{selected.Count}：{mod.Name}", done * 100.0 / selected.Count);
                }
                Log("全部提取完成。");
                SetProgress("就绪（提取完成）", 100);
            }
            finally
            {
                BtnExtract.IsEnabled = true;
            }
        }

        private static List<KeyedReplacement> LoadExisting(ModInfo mod, ExtractionOptions options)
        {
            var all = new List<KeyedReplacement>();
            foreach (var dir in mod.GetContentDirs(options.GameVersion))
            {
                string languages = Path.Combine(dir, "Languages");
                if (Directory.Exists(languages))
                    all.AddRange(ExistingTranslationLoader.LoadFromLanguagesDir(languages, options.Language, options.GameDataDir));
            }
            return all;
        }

        private static string SafeDirName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 60 ? name[..60] : name;
        }

        private void SetProgress(string text, double percent)
        {
            TxtProgress.Text = text;
            Progress.Value = Math.Clamp(percent, 0, 100);
        }

        private class UiLogger : IExtractLogger
        {
            private readonly MainWindow _w;
            public UiLogger(MainWindow w) => _w = w;
            public void Info(string message) => _w.Log(message);
            public void Warn(string message) => _w.Log("[警告] " + message);
            public void Error(string message) => _w.Log("[错误] " + message);
        }

        private static readonly Dictionary<string, (string endpoint, string model, bool needKey)> ProviderDefaults = new()
        {
            ["Player2"] = ("http://127.0.0.1:4315/v1/chat/completions", "Default", false),
            ["Google"] = ("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "gemini-2.0-flash", true),
            ["OpenAI"] = ("https://api.openai.com/v1/chat/completions", "gpt-4o-mini", true),
            ["DeepSeek"] = ("https://api.deepseek.com/v1/chat/completions", "deepseek-chat", true),
            ["Grok"] = ("https://api.x.ai/v1/chat/completions", "grok-3-mini", true),
            ["GLM"] = ("https://api.z.ai/api/paas/v4/chat/completions", "glm-4-flash", true),
            ["OpenRouter"] = ("https://openrouter.ai/api/v1/chat/completions", "openai/gpt-4o-mini", true),
            ["SiliconFlow"] = ("https://api.siliconflow.cn/v1/chat/completions", "", true),
            ["Local"] = ("http://localhost:1234/v1/chat/completions", "", false),
            ["Custom"] = ("", "", true),
        };

        private bool _providerUpdating;

        private void CmbProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProvider.SelectedItem is not ComboBoxItem item || item.Tag is not string key) return;
            if (_providerUpdating) return;
            _providerUpdating = true;
            var (endpoint, model, needKey) = ProviderDefaults[key];
            TxtEndpoint.Text = endpoint;
            TxtModel.Text = model;
            TxtApiKey.Clear();
            TxtApiKey.IsEnabled = needKey;
            TxtPlayer2Hint.Visibility = key == "Player2" ? Visibility.Visible : Visibility.Collapsed;
            _settings.AiProvider = key;
            _settings.Save();
            _providerUpdating = false;
            Log($"翻译AI 供应商：{((ComboBoxItem)CmbProvider.SelectedItem).Content}");
        }

        private void TxtEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_providerUpdating) return;
            _settings.AiEndpoint = TxtEndpoint.Text.Trim();
            _settings.Save();
        }

        private void TxtApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_providerUpdating) return;
            _settings.AiApiKey = TxtApiKey.Text.Trim();
            _settings.Save();
        }

        private void TxtModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_providerUpdating) return;
            _settings.AiModel = TxtModel.Text.Trim();
            _settings.Save();
        }

        private async void BtnTestConn_Click(object sender, RoutedEventArgs e)
        {
            BtnTestConn.IsEnabled = false;
            try
            {
                var settings = new RimTrans.Core.AI.AiSettings
                {
                    Provider = _settings.AiProvider,
                    Endpoint = _settings.AiEndpoint,
                    ApiKey = _settings.AiApiKey,
                    Model = _settings.AiModel,
                };
                Log($"测试连接：{_settings.AiProvider}（{RimTrans.Core.AI.EndpointNormalizer.Normalize(_settings.AiEndpoint)}）…");
                bool ok = await Task.Run(() => new RimTrans.Core.AI.AiClient(settings).TestConnectionAsync());
                if (ok)
                {
                    Log("测试连接成功。");
                    MessageBox.Show(this, "连接成功，AI 已就绪。", "测试连接", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (RimTrans.Core.AI.AiException ex)
            {
                Log($"[错误] {ex.Message}");
                MessageBox.Show(this, ex.Message, "测试连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log($"[错误] {ex.Message}");
                MessageBox.Show(this, ex.Message, "测试连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnTestConn.IsEnabled = true;
            }
        }

        private void BtnGlossary_Click(object sender, RoutedEventArgs e)
        {
            var win = new GlossaryWindow(_settings);
            win.SetLanguage(_settings.TargetLanguage);
            win.Owner = this;
            win.Show();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            new HelpWindow { Owner = this }.ShowDialog();
        }

        private void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            if (e.Uri.OriginalString == "checkupdate")
            {
                CheckForUpdate();
                return;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }

        private void QQGroup_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Clipboard.SetText("1568455");
                MessageBox.Show(this, "QQ群号已复制：1568455\n打开 QQ 搜索群号加入，获取帮助与最新消息。", "QQ群", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private async void CheckForUpdate()
        {
            Log("检查更新：当前 v1.0.0，更新源待发布后接入（GitHub Releases）。");
            try
            {
                // 更新源：GitHub Releases latest API（仓库发布后启用）
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.Add("User-Agent", "RimTranslator");
                var resp = await http.GetAsync("https://api.github.com/repos/Maiya0126/rimtranslator/releases/latest");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                    string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(tag) && tag.TrimStart('v', 'V') != "1.0.0")
                    {
                        if (MessageBox.Show(this, $"发现新版本 {tag}，是否打开下载页？", "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                        return;
                    }
                    MessageBox.Show(this, $"当前已是最新版本（v1.0.0）。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(this, "暂时无法获取更新信息（仓库尚未发布或网络不通）。可手动访问 GitHub Releases 页面。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "检查更新失败：" + ex.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Clear();

        private void Log(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
