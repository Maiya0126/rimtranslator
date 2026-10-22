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
            _loadingUi = true;
            LoadUiFromSettings();
            _loadingUi = false;
            Log("边缘译者 RimTranslator 已启动。");
            _ = WarmCloudGlossaryAsync();
            Loaded += (_, _) => _ = AutoScanAsync();
        }

        /// <summary>启动自动扫描（后台静默，不阻塞界面）。</summary>
        private async Task AutoScanAsync(bool force = false)
        {
            if (!force && (_allMods.Count > 0 || BtnScan.IsEnabled == false)) return;
            await Task.Delay(300);
            var sources = GetSelectedSources().ToList();
            if (sources.Count == 0)
            {
                Log("未找到可扫描的来源目录，请点「⚙ 目录…」设置或「⟳ 刷新列表」重试。");
                return;
            }
            BtnScan.IsEnabled = false;
            if (TxtCount != null) TxtCount.Text = "⟳ 正在自动扫描…（Mod 多时需十几秒，请稍候）";
            SetProgress("启动扫描…", 0);
            try
            {
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
                ApplyFilter();
                Log($"自动扫描完成：{mods.ParsedOk} 个 Mod（用时 {mods.ScanSeconds:F1}s）。");
                foreach (var g in _allMods.GroupBy(m => m.Source).OrderBy(g => g.Key))
                    Log($"  {g.Key}: {g.Count()} 个");
                SetProgress("就绪", 0);
            }
            finally
            {
                BtnScan.IsEnabled = true;
            }
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

            TxtAuthor.Text = _settings.TranslatorAuthor;
        }

        public class LanguageItem
        {
            public string Folder { get; set; } = "";
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }

        private void TxtAuthor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loadingUi) return;
            _settings.TranslatorAuthor = TxtAuthor.Text.Trim();
            _settings.Save();
        }

        private IEnumerable<(ModSource, string)> GetSelectedSources()
        {
            if (ChkWorkshop.IsChecked == true)
            {
                if (Directory.Exists(_settings.WorkshopDir))
                    yield return (ModSource.Workshop, _settings.WorkshopDir);
                foreach (var dir in _settings.ExtraWorkshopDirs)
                    if (Directory.Exists(dir))
                        yield return (ModSource.Workshop, dir);
            }
            if (ChkLocal.IsChecked == true && Directory.Exists(_settings.LocalModsDir))
                yield return (ModSource.Local, _settings.LocalModsDir);
            if (ChkData.IsChecked == true && Directory.Exists(_settings.GameDataDir))
                yield return (ModSource.GameData, _settings.GameDataDir);
            if (ChkCustom.IsChecked == true)
                foreach (var dir in _settings.CustomSources)
                    if (Directory.Exists(dir))
                        yield return (ModSource.Custom, dir);
        }

        private void BtnDirs_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings, async () =>
            {
                Log("目录设置已更新，自动重新扫描…");
                LoadUiFromSettings();
                await AutoScanAsync(force: true);
            })
            { Owner = this };
            win.ShowDialog();
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false;
            try
            {
                var sources = GetSelectedSources().ToList();
                Log($"开始扫描 {sources.Count} 个来源…");
                if (TxtCount != null) TxtCount.Text = "⟳ 正在扫描…";
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

        private bool _loadingUi;
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
                MessageBox.Show(this, "「生成独立汉化Mod」需要先在右侧选择保存目录（新 Mod 的存放位置）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string author = TxtAuthor.Text.Trim();
            if (useSkeleton && author.Length == 0)
            {
                MessageBox.Show(this, "「生成独立汉化Mod」需要先填写「译者名」——它将写入新 Mod 的 About.xml 作为作者署名（不会冒用原作者名）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAuthor.Focus();
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
                LanguageFolder = GameLanguageFinder.ResolveOutputFolderName(_settings.GameDataDir, _settings.TargetLanguage),
            };

            var logger = new UiLogger(this);
            BtnExtract.IsEnabled = false;
            BtnTranslate.IsEnabled = false;
            BtnTranslateSkeleton.IsEnabled = false;
            try
            {
                // 术语库优先级：自定义 > 云端（维护者持续更新）> 官方
                Log("构建术语库（自定义 + 云端 + 官方）…");
                SetProgress("构建术语库…", 2);
                var glossary = new Glossary();
                glossary.AddCustomRange(Glossary.LoadCustom(_settings.TargetLanguage));
                int customCount = glossary.Count;
                int cloudCount = CloudGlossary.Loaded ? CloudGlossary.Map.Count : 0;
                if (cloudCount == 0)
                {
                    try { await Task.Run(() => CloudGlossary.FetchAsync()); cloudCount = CloudGlossary.Map.Count; } catch { }
                }
                var official = await Task.Run(() => OfficialGlossaryBuilder.Build(_settings.GameDataDir, _settings.TargetLanguage));
                glossary.AddRange(official);
                Log($"术语库就绪：自定义 {customCount} 条，云端 {cloudCount} 条，官方 {official.Count} 条（优先级：自定义 > 云端 > 官方）");

                // A1 批量队列：全部 Mod 提取完 → 统一术语+AI → 一张大审阅表 → 一次写入
                var allItems = new List<RimTrans.Core.AI.TranslationItem>();
                var modCount = selected.Count;
                int step = 0;
                foreach (var mod in selected)
                {
                    step++;
                    Log($"── [{step}/{modCount}] 提取 [{mod.SourceDisplay}] {mod.Name}");
                    if (useSkeleton)
                    {
                        string skelDir = Path.Combine(outDir, SafeDirName(mod.Name));
                        SkeletonModBuilder.Build(skelDir, mod, options, author);
                        options.CustomDir = skelDir;
                        Log($"  已生成独立汉化Mod骨架：{skelDir}");
                    }

                    SetProgress($"提取 {step}/{modCount}：{mod.Name}", step * 5.0 / modCount);
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

                    // LoadFolders Mod 的 Languages 在版本子目录（如 1.6-7\Languages），逐一扫描全部内容目录
                    int scanned = 0;
                    if (useSkeleton)
                    {
                        var items = await Task.Run(() => RimTrans.Core.AI.TodoScanner.Scan(
                            Path.Combine(outDir, SafeDirName(mod.Name), "Languages"), options.Language));
                        foreach (var it in items) it.ModName = mod.Name;
                        allItems.AddRange(items);
                        scanned = items.Count;
                    }
                    else
                    {
                        foreach (var contentDir in mod.GetContentDirs(options.GameVersion))
                        {
                            var items = await Task.Run(() => RimTrans.Core.AI.TodoScanner.Scan(
                                Path.Combine(contentDir, "Languages"), options.Language));
                            foreach (var it in items) it.ModName = mod.Name;
                            allItems.AddRange(items);
                            scanned += items.Count;
                        }
                    }
                    if (scanned > 0)
                    {
                        Log($"  待翻译 {scanned} 条（累计 {allItems.Count}）");
                    }
                    else Log("  没有 TODO 条目，无需翻译。");
                }

                if (allItems.Count == 0)
                {
                    Log("所有 Mod 均无 TODO 条目，翻译流程结束。");
                    SetProgress("就绪（无待翻译内容）", 100);
                    return;
                }

                // 统一术语 + AI（3 路并发）
                Log($"共 {allItems.Count} 条待翻译，开始术语匹配与 AI 翻译（3 路并发）…");
                var progress = new Progress<(int done, int total, string stage)>(p =>
                {
                    SetProgress($"{p.stage} {p.done}/{p.total}", p.total == 0 ? 0 : p.done * 100.0 / p.total);
                });
                var translator = new RimTrans.Core.AI.BatchTranslator(aiClient, FriendlyLanguageName(options.Language));
                await Task.Run(() => translator.TranslateAsync(allItems, glossary, progress));

                int g = allItems.Count(i => i.Status == "glossary"), a = allItems.Count(i => i.Status == "ai"), f = allItems.Count(i => i.Status == "failed");
                Log($"翻译完成（{modCount} 个 Mod 合计）：术语 {g}，AI {a}，失败 {f}。打开统一审阅窗口…");
                SetProgress("审阅中", 100);

                var review = new ReviewWindow(allItems, msg => Log(msg), skipBackup: useSkeleton, translator: translator, glossary: glossary)
                {
                    Owner = this,
                };
                review.ShowDialog();
                Log($"批量翻译流程结束（{modCount} 个 Mod）。");
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
                BtnTranslate.IsEnabled = true;
                BtnTranslateSkeleton.IsEnabled = true;
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
                Mode = OutputMode.ModDir,
                LanguageFolder = GameLanguageFinder.ResolveOutputFolderName(_settings.GameDataDir, _settings.TargetLanguage),
            };

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
            BtnTranslate.IsEnabled = false;
            BtnTranslateSkeleton.IsEnabled = false;
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
                        SkeletonModBuilder.Build(skelDir, mod, options, TxtAuthor.Text.Trim());
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
                BtnTranslate.IsEnabled = true;
                BtnTranslateSkeleton.IsEnabled = true;
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
            if (Dispatcher.CheckAccess())
            {
                TxtProgress.Text = text;
                Progress.Value = Math.Clamp(percent, 0, 100);
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    TxtProgress.Text = text;
                    Progress.Value = Math.Clamp(percent, 0, 100);
                });
            }
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
            string current = RimTrans.Core.Updater.CurrentVersion;
            Log($"检查更新：当前 v{current}…");
            try
            {
                var info = await Task.Run(() => RimTrans.Core.Updater.CheckAsync());
                if (info == null || string.IsNullOrEmpty(info.Tag))
                {
                    MessageBox.Show(this, "暂时无法获取更新信息（仓库尚未发布或网络不通）。可手动访问 GitHub Releases 页面。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!RimTrans.Core.Updater.IsNewer(info.Tag, current))
                {
                    MessageBox.Show(this, $"当前已是最新版本（v{current}）。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string sizeText = info.Size > 0 ? $"（约 {info.Size / 1024 / 1024} MB）" : "";
                var choice = MessageBox.Show(this,
                    $"发现新版本 {info.Tag}{sizeText}。\n\n是 — 立即下载并自动更新（下载完成后程序会自动退出替换并重启）\n否 — 打开发布页手动查看",
                    "检查更新", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
                if (choice == MessageBoxResult.No)
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true }); } catch { }
                    return;
                }
                if (choice != MessageBoxResult.Yes) return;

                // 应用内下载 + 自动替换
                SetProgress("下载更新…", 0);
                var progress = new Progress<(long done, long total)>(p =>
                {
                    double pct = p.total > 0 ? p.done * 100.0 / p.total : 0;
                    SetProgress($"下载更新 {p.done / 1024 / 1024}/{p.total / 1024 / 1024} MB", pct);
                });
                string downloaded = await Task.Run(() => RimTrans.Core.Updater.DownloadAsync(info, progress));
                Log($"更新包下载完成：{info.Tag}。程序将退出并自动替换重启…");
                RimTrans.Core.Updater.LaunchSelfReplace(downloaded,
                    System.Diagnostics.Process.GetCurrentProcess().Id);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "检查更新失败：" + ex.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Clear();

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            if (Dispatcher.CheckAccess())
            {
                TxtLog.AppendText(line);
                TxtLog.ScrollToEnd();
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    TxtLog.AppendText(line);
                    TxtLog.ScrollToEnd();
                });
            }
        }
    }
}
