using Avalonia.Media.Imaging;
using AvaloniaImageSelect.Services;
using AvaloniaImageSelect.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ursa.Controls;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SqliteService _service;
        private readonly MediaAnimationPlayer _animationPlayer = new();
        private CancellationTokenSource? _loadCts;
        private string _imageFolder;
        private string _imageDestinationFolder;
        private Dictionary<int, string> _images = new();
        private bool _deleteWhenClose;
        private MediaKind _currentMediaKind = MediaKind.Image;
        private bool _previewWarningShown;

        //public Animation NextAnimation { get; set; }
        //public Animation PreAnimation { get; set; }

        public MainWindowViewModel()
        {
            _service = App.Provider.GetRequiredService<SqliteService>();
            _imageFolder = _service.GetImageFolder();
            if (!string.IsNullOrEmpty(_imageFolder))
            {
                _imageDestinationFolder = _service.GetDestinationImageFolder();
                _deleteWhenClose = _service.GetDeleteWhenClose();
                if (Directory.Exists(_imageFolder))
                {
                    // 支持静态图片、动图、HEIC 以及视频
                    var files = Directory.EnumerateFiles(_imageFolder, "*.*")
                        .Where(file => MediaTypes.IsSupported(file))
                        .ToArray();
                    if (files.Length == 0)
                    {
                        MessageBox.ShowAsync("没有找到图片或视频文件", "", MessageBoxIcon.Warning, MessageBoxButton.OK);
                        return;
                    }

                    for (int i = 0; i < files.Length; i++)
                    {
                        _images.Add(i + 1, files[i]);
                    }
                }
            }

            if (_images.Count == 0)
            {
                return;
            }

            PrefixDate = DateTime.Now.ToString("yyyyMMdd");
            LoadCurrentMedia();
        }

        [ObservableProperty]
        private Bitmap _currentImage;

        [ObservableProperty]
        private string _prefixDate;

        private int _currentIndex = 1;

        public int CurrentIndex
        {
            get => _currentIndex;
            set
            {
                SetProperty(ref _currentIndex, value);
                LoadCurrentMedia();
            }
        }

        [ObservableProperty]
        private string _title;

        public void SetTitle()
        {
            if (_images.Count > 0 && _images.TryGetValue(CurrentIndex, out string? filename))
            {
                // 动图/视频在标题上标注类型，方便快速分辨。
                var kindName = MediaTypes.GetDisplayName(_currentMediaKind);
                var suffix = string.IsNullOrEmpty(kindName) ? string.Empty : "[" + kindName + "]";
                Title = $"{filename}({CurrentIndex}/{_images.Count}){suffix}";
            }
        }

        [RelayCommand]
        private void MenuSetting()
        {
            var options = new OverlayDialogOptions()
            {
                FullScreen = false,
                HorizontalAnchor = HorizontalPosition.Center,
                VerticalAnchor = VerticalPosition.Center,
                HorizontalOffset = null,
                VerticalOffset = null,
                Mode = DialogMode.None,
                Buttons = DialogButton.None,
                Title = "设置",
                CanLightDismiss = false,
                CanDragMove = false,
                IsCloseButtonVisible = true,
                CanResize = false,
                StyleClass = null,
            };
            OverlayDialog.ShowModal<UserSettingView, UserSettingViewModel>(new UserSettingViewModel(), "LocalHost", options);
        }

        [RelayCommand]
        private void NextImage(ImageViewer image)
        {
            if (CurrentIndex < _images.Count)
            {
                // CurrentIndex 的 setter 会触发加载，这里不需要重复调用。
                CurrentIndex++;
            }
        }

        [RelayCommand]
        private void PreImage(ImageViewer image)
        {
            if (CurrentIndex > 1)
            {
                CurrentIndex--;
            }
        }

        [RelayCommand]
        private void KeyEnter()
        {
            if (!_images.TryGetValue(CurrentIndex, out var currentFile))
            {
                return;
            }

            var fileName = SetCurrentImageFileName(currentFile);
            File.Copy(currentFile, fileName, true);
            MessageBox.ShowAsync("已添加当前照片", "", MessageBoxIcon.Success, MessageBoxButton.OK);
        }

        private string SetCurrentImageFileName(string currentFileName)
        {
            var extension = System.IO.Path.GetExtension(currentFileName);
            // 编号需要把动图、视频一并算进去，避免与已有文件重名。
            var filesCount = Directory.GetFiles(_imageFolder, _prefixDate + "*.*", System.IO.SearchOption.TopDirectoryOnly)
                .Count(MediaTypes.IsSupported);
            if (filesCount == 0)
            {
                return System.IO.Path.Combine(_imageFolder, _prefixDate + extension);
            }
            return System.IO.Path.Combine(_imageFolder, _prefixDate + "-" + (filesCount) + extension);
        }

        /// <summary>
        /// 加载当前序号对应的文件：静态图直接显示，动图/视频交给播放器循环播放。
        /// </summary>
        private void LoadCurrentMedia()
        {
            if (!_images.TryGetValue(CurrentIndex, out var path))
            {
                return;
            }

            // 切换时先停掉上一张的动画，并取消还没完成的解码任务。
            CancelPendingLoad();
            var cts = new CancellationTokenSource();
            _loadCts = cts;

            _animationPlayer.Stop();
            _currentMediaKind = MediaKind.Image;
            SetTitle();

            _ = LoadMediaAsync(path, cts);
        }

        private async Task LoadMediaAsync(string path, CancellationTokenSource cts)
        {
            MediaPreview preview;
            try
            {
                // 解码（尤其是 FFmpeg 抽帧）比较耗时，放到后台线程，避免卡住界面。
                preview = await Task.Run(() => MediaLoader.Load(path, cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                return;
            }

            // 期间用户又切换了图片，丢弃这次的结果。
            if (cts.IsCancellationRequested)
            {
                return;
            }

            _currentMediaKind = preview.Kind;

            if (preview.IsAnimated)
            {
                _animationPlayer.Play(preview.Frames!, bitmap => CurrentImage = bitmap);
            }
            else if (preview.StaticImage != null)
            {
                CurrentImage = preview.StaticImage;
            }

            if (!string.IsNullOrEmpty(preview.ErrorMessage))
            {
                ShowPreviewWarningOnce(preview.ErrorMessage);
            }

            GC.Collect();
            SetTitle();
        }

        private void CancelPendingLoad()
        {
            if (_loadCts == null)
            {
                return;
            }

            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }

        /// <summary>
        /// 缺少 FFmpeg 之类的提示只弹一次，避免翻看照片时被反复打扰。
        /// </summary>
        private void ShowPreviewWarningOnce(string message)
        {
            if (_previewWarningShown)
            {
                return;
            }

            _previewWarningShown = true;
            _ = MessageBox.ShowAsync(message, "", MessageBoxIcon.Warning, MessageBoxButton.OK);
        }

        public void Closing()
        {
            CancelPendingLoad();
            _animationPlayer.Stop();

            if (_deleteWhenClose)
            {
                var allFile = Directory.GetFiles(_imageFolder);
                foreach (var file in allFile)
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    if (fileName.StartsWith(_prefixDate))
                    {
                        var destFileName = System.IO.Path.Combine(_imageDestinationFolder, fileName);
                        if (!Directory.Exists(_imageDestinationFolder))
                        {
                            Directory.CreateDirectory(_imageDestinationFolder);
                        }
                        if (!File.Exists(destFileName))
                        {
                            File.Copy(file, destFileName);
                        }
                    }
                    else
                    {
                        FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }
                }
            }

            // 清理动态照片抽取出的临时视频，并释放播放器。
            MotionPhotoExtractor.Cleanup();
            _animationPlayer.Dispose();
        }
    }
}
