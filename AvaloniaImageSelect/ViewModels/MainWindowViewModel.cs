using Avalonia.Controls.Shapes;
using Avalonia.Media.Imaging;
using AvaloniaImageSelect.Services;
using AvaloniaImageSelect.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ursa.Controls;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SqliteService _service;
        private string _imageFolder;
        private string _imageDestinationFolder;
        private Dictionary<int, string> _images = new();
        //private int _currentIndex = 1;
        private bool _deleteWhenClose;

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
                    //string[] extensions = { ".JPG" };
                    string[] extensions = { ".JPG", ".HEIC", ".MP4" };

                    var files = Directory.EnumerateFiles(_imageFolder, "*.*")
                        .Where(file => extensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                    if (files.Length > 0)
                    {
                        for (int i = 0; i < files.Length; i++)
                        {
                            _images.Add(i + 1, files[i]);
                        }
                    }
                    else
                    {
                        MessageBox.ShowAsync("没有找到JPG文件", "", MessageBoxIcon.Warning, MessageBoxButton.OK);
                        return;
                    }
                }
                CurrentImage = GetBitmap(_images[1]);
                SetTitle();
                PrefixDate = DateTime.Now.ToString("yyyyMMdd");
            }
        }

        private Bitmap GetBitmap(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName);
            if (string.Equals(extension, ".HEIC", StringComparison.OrdinalIgnoreCase))
            {
                using (var magickImage = new MagickImage(fileName))
                {
                    Avalonia.Media.Imaging.Bitmap avaloniaBitmap = magickImage.ToWriteableBitmap();
                    return avaloniaBitmap;
                }
            }
            if (string.Equals(extension, ".MP4", StringComparison.OrdinalIgnoreCase))
            {
                return GetVideoFirstFrame(fileName);
            }
            // JPG文件：检查是否为小米动图（末尾嵌入了MP4视频）
            if (string.Equals(extension, ".JPG", StringComparison.OrdinalIgnoreCase))
            {
                int videoOffset = GetEmbeddedVideoOffset(fileName);
                if (videoOffset >= 0)
                {
                    return GetVideoFirstFrameAtOffset(fileName, videoOffset);
                }
            }
            return new Avalonia.Media.Imaging.Bitmap(fileName);
        }

        /// <summary>
        /// 从MP4视频文件提取第一帧
        /// </summary>
        private Bitmap GetVideoFirstFrame(string fileName)
        {
            try
            {
                using (var collection = new MagickImageCollection())
                {
                    var settings = new MagickReadSettings
                    {
                        FrameIndex = 0,
                        FrameCount = 1
                    };
                    collection.Read(fileName, settings);

                    if (collection.Count > 0)
                    {
                        using (var frame = collection[0])
                        {
                            frame.Format = MagickFormat.Jpeg;
                            return frame.ToWriteableBitmap();
                        }
                    }
                }
            }
            catch { }
            return new Avalonia.Media.Imaging.Bitmap(fileName);
        }

        /// <summary>
        /// 从小米动图（JPG+嵌入视频）中提取视频第一帧
        /// </summary>
        private Bitmap GetVideoFirstFrameAtOffset(string fileName, int videoOffset)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(fileName);
                byte[] videoData = new byte[fileData.Length - videoOffset];
                Array.Copy(fileData, videoOffset, videoData, 0, videoData.Length);

                using (var collection = new MagickImageCollection())
                {
                    var settings = new MagickReadSettings
                    {
                        FrameIndex = 0,
                        FrameCount = 1
                    };
                    collection.Read(videoData, settings);

                    if (collection.Count > 0)
                    {
                        using (var frame = collection[0])
                        {
                            frame.Format = MagickFormat.Jpeg;
                            return frame.ToWriteableBitmap();
                        }
                    }
                }
            }
            catch { }
            // 兜底：加载JPG静态部分
            return new Avalonia.Media.Imaging.Bitmap(fileName);
        }

        /// <summary>
        /// 检测小米动图中嵌入的MP4视频数据偏移量
        /// 小米动图格式：标准JPG数据 + MP4视频数据（ftyp标记）
        /// </summary>
        private int GetEmbeddedVideoOffset(string fileName)
        {
            try
            {
                byte[] data = File.ReadAllBytes(fileName);
                byte[] ftyp = { (byte)'f', (byte)'t', (byte)'y', (byte)'p' };
                // 从文件末尾向前搜索，小米动图的视频数据在文件尾部
                for (int i = data.Length - 4; i >= 0; i--)
                {
                    if (data[i] == ftyp[0] && data[i + 1] == ftyp[1]
                        && data[i + 2] == ftyp[2] && data[i + 3] == ftyp[3])
                    {
                        // ftyp 前4字节是box size，回退4字节得到MP4起始位置
                        int offset = i - 4;
                        if (offset >= 0)
                        {
                            return offset;
                        }
                    }
                }
            }
            catch { }
            return -1;
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
                SetImage();
            }

        }




        [ObservableProperty]
        private string _title;

        public void SetTitle()
        {
            if (_images.Count > 0 && _images.TryGetValue(CurrentIndex, out string? filename))
            {
                Title = $"{filename}({CurrentIndex}/{_images.Count})";
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
                CurrentIndex++;
            }

            SetImage();
        }

        [RelayCommand]
        private void PreImage(ImageViewer image)
        {
            if (CurrentIndex > 1)
            {
                CurrentIndex--;
            }

            SetImage();
        }

        [RelayCommand]
        private void KeyEnter()
        {
            var fileName = SetCurrentImageFileName(_images[CurrentIndex]);
            File.Copy(_images[CurrentIndex], fileName, true);
            MessageBox.ShowAsync("已添加当前照片", "", MessageBoxIcon.Success, MessageBoxButton.OK);
        }

        private string SetCurrentImageFileName(string currentFileName)
        {
            var extension = System.IO.Path.GetExtension(currentFileName);
            var heicFiles = Directory.GetFiles(_imageFolder, _prefixDate + "*.HEIC", System.IO.SearchOption.TopDirectoryOnly);
            var jpgFiles = Directory.GetFiles(_imageFolder, _prefixDate + "*.JPG", System.IO.SearchOption.TopDirectoryOnly);
            var mp4Files = Directory.GetFiles(_imageFolder, _prefixDate + "*.MP4", System.IO.SearchOption.TopDirectoryOnly);
            var filesCount = heicFiles.Length + jpgFiles.Length + mp4Files.Length;
            if (filesCount == 0)
            {
                return System.IO.Path.Combine(_imageFolder, _prefixDate + extension);
            }
            return System.IO.Path.Combine(_imageFolder, _prefixDate + "-" + (filesCount) + extension);
        }

        private void SetImage()
        {
            CurrentImage = GetBitmap(_images[CurrentIndex]);
            GC.Collect();
            //PreAnimation.RunAsync(image);
            SetTitle();
        }
        public void Closing()
        {
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
        }
    }
}
