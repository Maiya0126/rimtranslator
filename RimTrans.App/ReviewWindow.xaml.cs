using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RimTrans.Core.AI;

namespace RimTrans.App
{
    public partial class ReviewWindow : Window
    {
        private readonly List<TranslationItem> _items;
        private readonly Action<string> _log;
        private readonly bool _skipBackup;

        public ReviewWindow(List<TranslationItem> items, Action<string> log, bool skipBackup = false)
        {
            InitializeComponent();
            _items = items;
            _log = log;
            _skipBackup = skipBackup;
            Grid.ItemsSource = _items;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int glossary = _items.Count(i => i.Status == "glossary");
            int ai = _items.Count(i => i.Status == "ai");
            int failed = _items.Count(i => i.Status == "failed");
            int todo = _items.Count(i => i.Status == "TODO");
            TxtSummary.Text = $"共 {_items.Count} 条：术语回填 {glossary}，AI 翻译 {ai}，失败 {failed}，未处理 TODO {todo}。" +
                "双击译文单元格可编辑；只写入非空译文，TODO/失败项保持原样。";
        }

        private void BtnRetryFailed_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "请关闭本窗口后重新执行翻译流程。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
