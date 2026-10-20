using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RimTrans.Core;
using RimTrans.Core.AI;

namespace RimTrans.App
{
    public partial class ReviewWindow : Window
    {
        private readonly List<TranslationItem> _items;
        private readonly Action<string> _log;
        private readonly bool _skipBackup;
        private readonly BatchTranslator? _translator;
        private readonly Glossary? _glossary;

        public ReviewWindow(List<TranslationItem> items, Action<string> log, bool skipBackup = false,
            BatchTranslator? translator = null, Glossary? glossary = null)
        {
            InitializeComponent();
            _items = items;
            _log = log;
            _skipBackup = skipBackup;
            _translator = translator;
            _glossary = glossary;
            if (translator == null) BtnRetryFailed.IsEnabled = false;
            Grid.ItemsSource = _items;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int glossary = _items.Count(i => i.Status == "glossary");
            int ai = _items.Count(i => i.Status == "ai");
            int failed = _items.Count(i => i.Status == "failed");
            int todo = _items.Count(i => i.Status == "TODO");
            string mods = _items.Select(i => i.ModName).Where(m => m.Length > 0).Distinct().Count().ToString();
            TxtSummary.Text = $"共 {_items.Count} 条（{mods} 个 Mod）：术语回填 {glossary}，AI 翻译 {ai}，失败 {failed}，未处理 TODO {todo}。" +
                "双击译文单元格可编辑；只写入非空译文，TODO/失败项保持原样。";
        }

        private async void BtnRetryFailed_Click(object sender, RoutedEventArgs e)
        {
            if (_translator == null) return;
            int failedCount = _items.Count(i => i.Status == "failed");
            if (failedCount == 0)
            {
                MessageBox.Show(this, "没有失败项。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            BtnRetryFailed.IsEnabled = false;
            try
            {
                _log($"审阅窗原地重试 {failedCount} 条失败项…");
                await Task.Run(() => _translator.RetryFailedAsync(_items, _glossary));
                Grid.Items.Refresh();
                UpdateSummary();
                int stillFailed = _items.Count(i => i.Status == "failed");
                MessageBox.Show(this, $"重试完成：成功 {failedCount - stillFailed} 条，仍失败 {stillFailed} 条。", "重试", MessageBoxButton.OK,
                    stillFailed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                _log($"重试完成：成功 {failedCount - stillFailed}，仍失败 {stillFailed}。");
            }
            finally
            {
                BtnRetryFailed.IsEnabled = true;
            }
        }

        private void BtnMarkAllTodo_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items.Where(i => i.Status != "TODO"))
            {
                item.Final = "";
                item.Status = "TODO";
            }
            Grid.Items.Refresh();
            UpdateSummary();
        }

        private void BtnWrite_Click(object sender, RoutedEventArgs e)
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            // 用户编辑过的条目标记为 edited
            int written = TodoScanner.WriteBack(_items, _skipBackup);
            _log(_skipBackup
                ? $"已写回 {written} 条译文（独立汉化Mod，全新文件，无需备份）。"
                : $"已写回 {written} 条译文（覆盖文件自动生成 .bak 备份）。");
            MessageBox.Show(this, $"写入完成：{written} 条。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
