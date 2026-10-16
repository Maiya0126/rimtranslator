using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RimTrans.Core;

namespace RimTrans.App
{
    public partial class GlossaryWindow : Window
    {
        private readonly AppSettings _settings;
        private List<GlossaryEntry> _entries = new();
        private List<GlossaryEntry> _official = new();
        private List<GlossaryEntry> _officialView = new();
        private bool _loading;

        public GlossaryWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            var langs = GameLanguageFinder.GetLanguages(_settings.GameDataDir);
            CmbLangsInit(langs);
        }

        private void CmbLangsInit(List<string> langs)
        {
            // 语言选择放标题下第一行：这里用官方页的过滤框旁，不再单独下拉——通过主窗口语言切换。
            TxtOfficialStat.Text = _official.Count == 0 ? "尚未构建" : $"共 {_official.Count} 条";
        }

        internal void SetLanguage(string lang)
        {
            if (_loading) return;
            _loading = true;
            _entries = Glossary.LoadCustom(lang);
            Grid.ItemsSource = _entries;
            UpdateStat();
            _loading = false;
        }

        private void UpdateStat() => TxtStat.Text = $"共 {_entries.Count} 条（未保存的修改以「保存」为准）";

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _entries.Add(new GlossaryEntry());
            Grid.Items.Refresh();
            Grid.ScrollIntoView(Grid.Items[^1]);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var sel = Grid.SelectedItems.Cast<GlossaryEntry>().ToList();
            foreach (var s in sel) _entries.Remove(s);
            Grid.Items.Refresh();
            UpdateStat();
        }

        private async void BtnLoadOfficial_Click(object sender, RoutedEventArgs e)
        {
            BtnLoadOfficial.IsEnabled = false;
            TxtOfficialStat.Text = "载入中…（首次可能数十秒）";
            try
            {
                string lang = _settings.TargetLanguage;
                _official = await Task.Run(() => OfficialGlossaryBuilder.Build(_settings.GameDataDir, lang));
                _official = _official.OrderByDescending(x => x.En.Length).ToList();
                ApplyOfficialFilter();
                TxtOfficialStat.Text = $"共 {_official.Count} 条（{lang}）— 已显示前 {_officialView.Count} 条，可在右侧搜索";
            }
            catch (Exception ex)
            {
                TxtOfficialStat.Text = "载入失败: " + ex.Message;
            }
            finally
            {
                BtnLoadOfficial.IsEnabled = true;
            }
        }

        private void TxtOfficialSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_official.Count > 0) ApplyOfficialFilter();
        }

        private void ApplyOfficialFilter()
        {
            string q = TxtOfficialSearch.Text?.Trim() ?? "";
            _officialView = string.IsNullOrEmpty(q)
                ? _official.Take(300).ToList()
                : _official.Where(x => x.En.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || x.Translation.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(300).ToList();
            GridOfficial.ItemsSource = _officialView;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            _entries = _entries.Where(x => !string.IsNullOrWhiteSpace(x.En) && !string.IsNullOrWhiteSpace(x.Translation)).ToList();
            Glossary.SaveCustom(_settings.TargetLanguage, _entries);
            Grid.ItemsSource = _entries;
            Grid.Items.Refresh();
            UpdateStat();
            MessageBox.Show(this, $"已保存 {_entries.Count} 条。翻译时本词库优先于官方术语库。", "保存完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private async void BtnCloudRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnCloudRefresh.IsEnabled = false;
            TxtCloudStat.Text = "拉取中…";
            try
            {
                var (count, msg) = await Task.Run(() => CloudGlossary.FetchAsync(force: true));
                TxtCloudStat.Text = $"{msg}：{count} 条（缓存于 {CloudGlossary.CachedAt:MM-dd HH:mm}）";
                ApplyCloudFilter();
            }
            catch (Exception ex)
            {
                TxtCloudStat.Text = "拉取失败：" + ex.Message + "（无网时翻译会自动用上次缓存）";
            }
            finally
            {
                BtnCloudRefresh.IsEnabled = true;
            }
        }

        private void TxtCloudSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CloudGlossary.Loaded) ApplyCloudFilter();
        }

        private void ApplyCloudFilter()
        {
            string q = TxtCloudSearch.Text?.Trim() ?? "";
            IEnumerable<KeyValuePair<string, string>> query = CloudGlossary.Map;
            if (q.Length > 0)
                query = query.Where(kv => kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || kv.Value.Contains(q, StringComparison.OrdinalIgnoreCase));
            GridCloud.ItemsSource = query.Take(300).ToList();
        }
    }
}
