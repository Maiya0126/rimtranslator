using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RimTrans.Core;

namespace RimTrans.App
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly Action _onSaved;
        private bool _loading = true;

        public SettingsWindow(AppSettings settings, Action onSaved)
        {
            InitializeComponent();
            _settings = settings;
            _onSaved = onSaved;
            Load();
            _loading = false;
        }

        private void Load()
        {
            TxtWorkshop.Text = _settings.WorkshopDir;
            LstExtraWorkshop.ItemsSource = _settings.ExtraWorkshopDirs.ToList();
            TxtLocal.Text = _settings.LocalModsDir;
            TxtData.Text = _settings.GameDataDir;
            ValidateWorkshop();
        }

        private void ValidateWorkshop()
        {
            string dir = TxtWorkshop.Text.Trim();
            TxtWorkshopHint.Text = Directory.Exists(dir)
                ? ""
                : "⚠ 目录不存在，创意工坊来源将不可用（点「自动检测」可找回）";
        }

        private void TxtWorkshop_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            ValidateWorkshop();
        }

        private void BtnBrowseWorkshop_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择创意工坊目录（…\\steamapps\\workshop\\content\\294100）" };
            if (dlg.ShowDialog(this) == true) TxtWorkshop.Text = dlg.FolderName;
        }

        private void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            BtnDetect.IsEnabled = false;
            try
            {
                var found = SteamLocator.FindWorkshopDirs();
                if (found.Count == 0)
                {
                    MessageBox.Show(this, "未在任何 Steam 库中找到 RimWorld（app 294100）的工坊内容。\n请确认已订阅模组，或手动「浏览」选择。", "自动检测", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                TxtWorkshop.Text = found[0];
                var extras = _settings.ExtraWorkshopDirs.ToList();
                foreach (var d in found.Skip(1))
                    if (!extras.Contains(d, StringComparer.OrdinalIgnoreCase))
                        extras.Add(d);
                LstExtraWorkshop.ItemsSource = extras;
                MessageBox.Show(this, $"检测到 {found.Count} 个含 RimWorld 工坊内容的 Steam 库：\n\n" + string.Join("\n", found), "自动检测", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { BtnDetect.IsEnabled = true; }
        }

        private void BtnAddExtra_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择另一个 Steam 库的工坊目录" };
            if (dlg.ShowDialog(this) == true)
            {
                var extras = (LstExtraWorkshop.ItemsSource as List<string> ?? new List<string>()).ToList();
                if (!extras.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase))
                    extras.Add(dlg.FolderName);
                LstExtraWorkshop.ItemsSource = extras;
            }
        }

        private void BtnRemoveExtra_Click(object sender, RoutedEventArgs e)
        {
            if (LstExtraWorkshop.SelectedItem is not string sel) return;
            var extras = (LstExtraWorkshop.ItemsSource as List<string> ?? new List<string>()).ToList();
            extras.Remove(sel);
            LstExtraWorkshop.ItemsSource = extras;
        }

        private void BtnBrowseLocal_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择本地 Mods 目录（RimWorld\\Mods）" };
            if (dlg.ShowDialog(this) == true) TxtLocal.Text = dlg.FolderName;
        }

        private void BtnBrowseData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择游戏 Data 目录（RimWorld\\Data）" };
            if (dlg.ShowDialog(this) == true) TxtData.Text = dlg.FolderName;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string ws = TxtWorkshop.Text.Trim();
            if (ws.Length > 0 && !Directory.Exists(ws))
            {
                if (MessageBox.Show(this, $"主工坊目录不存在：\n{ws}\n\n仍要保存吗？（创意工坊来源将不可用）", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            bool changed = ws != _settings.WorkshopDir
                || TxtLocal.Text.Trim() != _settings.LocalModsDir
                || TxtData.Text.Trim() != _settings.GameDataDir
                || !(LstExtraWorkshop.ItemsSource as List<string> ?? new List<string>()).SequenceEqual(_settings.ExtraWorkshopDirs, StringComparer.OrdinalIgnoreCase);

            _settings.WorkshopDir = ws;
            _settings.ExtraWorkshopDirs = (LstExtraWorkshop.ItemsSource as List<string> ?? new List<string>()).ToList();
            _settings.LocalModsDir = TxtLocal.Text.Trim();
            _settings.GameDataDir = TxtData.Text.Trim();
            _settings.Save();

            if (changed) _onSaved();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
