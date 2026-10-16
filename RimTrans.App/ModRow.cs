using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RimTrans.Core;

namespace RimTrans.App
{
    /// <summary>
    /// Mod 列表行视图模型：包装 ModInfo + 异步封面缩略图（内存缓存 + 后台解码；屏外行由 DataGrid 虚拟化不渲染）。
    /// </summary>
    public class ModRow : INotifyPropertyChanged
    {
        public ModInfo Mod { get; }
        public ModRow(ModInfo mod) => Mod = mod;

        public string Name => Mod.Name;
        public string PackageId => Mod.PackageId;
        public string SourceDisplay => Mod.SourceDisplay;
        public string VersionsDisplay => Mod.VersionsDisplay;
        public string LanguagesDisplay => Mod.LanguagesDisplay;
        public string PublishedFileId => Mod.PublishedFileId;

        private ImageSource? _preview;
        private static readonly Dictionary<string, ImageSource?> PreviewCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _previewLoading;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ImageSource? Preview
        {
            get
            {
                if (_preview != null) return _preview;
                if (_previewLoading) return null;
                string key = Mod.RootDir;
                if (PreviewCache.ContainsKey(key))
                {
                    _preview = PreviewCache[key];
                    return _preview;
                }
                _previewLoading = true;
                string file = Path.Combine(Mod.RootDir, "About", "Preview.png");
                if (!File.Exists(file))
                {
                    _previewLoading = false;
                    PreviewCache[key] = null;
                    return null;
                }
                _ = Task.Run(async () =>
                {
                    ImageSource? src = null;
                    try
                    {
                        var thumb = new BitmapImage();
                        using var fs = File.OpenRead(file);
                        thumb.BeginInit();
                        thumb.CacheOption = BitmapCacheOption.OnLoad;
                        thumb.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        thumb.DecodePixelWidth = 120;
                        thumb.StreamSource = fs;
                        thumb.EndInit();
                        thumb.Freeze();
                        src = thumb;
                    }
                    catch { }
                    PreviewCache[key] = src;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _preview = src;
                        _previewLoading = false;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
                    });
                });
                return null;
            }
        }
    }
}
