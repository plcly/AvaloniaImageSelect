using Avalonia.Media.Imaging;
using AvaloniaImageSelect.Services;
using AvaloniaImageSelect.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using LibVLCSharp.Shared;
using LibVLCSharp.Avalonia;
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
        private bool _deleteWhenClose;
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;

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

        [ObservableProperty]
        private bool _isVideo;

        [ObservableProperty]
        private bool _isPlaying;

        private MediaPlayer? _mediaPlayerProp;
        public MediaPlayer? MediaPlayer
        {
            get => _mediaPlayerProp;
            set => SetProperty(ref _mediaPlayerProp, value);
        }

        private Bitmap GetBitmap(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName);
            if (string.Equals(extension, ".HEIC", StringComparison.OrdinalIgnoreCase))
            {
                IsVideo = false;
                using (var magickImage = new MagickImage(fileName))
                {
                    return magickImage.ToWriteableBitmap();
                }
            }
            if (string.Equals(extension, ".MP4", StringComparison.OrdinalIgnoreCase))
            {
                IsVideo = true;
                return GetVideoFirstFrame(fileName);
            }
            // JPG文件：检查是否为小米动图（末尾嵌入了MP4视频）
            if (string.Equals(extension, ".JPG", StringComparison.OrdinalIgnoreCase))
            {
                int videoOffset = GetEmbeddedVideoOffset(fileName);
                if (videoOffset >= 0)
                {
                    IsVideo = true;
                    return GetVideoFirstFrameAtOffset(fileName, videoOffset);
                }
            }
            IsVideo = false;
            return new Avalonia.Media.Imaging.Bitmap(fileName);
        }

        /// <summary>
        /// 从MP4视频文件提取第一帧（通过FFmpeg）
        /// </summary>
        private Bitmap GetVideoFirstFrame(string fileName)
        {
            var tempPng = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{fileName}\" -vframes 1 -f image2 \"{tempPng}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                }
                if (File.Exists(tempPng))
                {
                    var bmp = new Avalonia.Media.Imaging.Bitmap(tempPng);
                    return bmp;
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
            }
            return CreateVideoPlaceholder();
        }

        /// <summary>
        /// 从小米动图（JPG+嵌入视频）中提取视频第一帧（通过FFmpeg）
        /// </summary>
        private Bitmap GetVideoFirstFrameAtOffset(string fileName, int videoOffset)
        {
            var tempMp4 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
            var tempPng = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                byte[] fileData = File.ReadAllBytes(fileName);
                byte[] videoData = new byte[fileData.Length - videoOffset];
                Array.Copy(fileData, videoOffset, videoData, 0, videoData.Length);
                File.WriteAllBytes(tempMp4, videoData);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{tempMp4}\" -vframes 1 -f image2 \"{tempPng}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                }
                if (File.Exists(tempPng))
                {
                    return new Avalonia.Media.Imaging.Bitmap(tempPng);
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
            }
            return new Avalonia.Media.Imaging.Bitmap(fileName);
        }

        /// <summary>
        /// 生成视频占位图（当FFmpeg不可用时）
        /// </summary>
        private Bitmap CreateVideoPlaceholder()
        {
            const int w = 400, h = 300;
            using (var image = new MagickImage(MagickColors.DarkGray, w, h))
            {
                image.Annotate("Video", Gravity.Center);
                return image.ToWriteableBitmap();
            }
        }

        /// <summary>
        /// 检测小米动图中嵌入的MP4视频数据偏移量
        /// </summary>
        private int GetEmbeddedVideoOffset(string fileName)
        {
            try
            {
                byte[] data = File.ReadAllBytes(fileName);
                byte[] ftyp = { (byte)'f', (byte)'t', (byte)'y', (byte)'p' };
                for (int i = data.Length - 4; i >= 0; i--)
                {
                    if (data[i] == ftyp[0] && data[i + 1] == ftyp[1]
                        && data[i + 2] == ftyp[2] && data[i + 3] == ftyp[3])
                    {
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
        private void NextImage()
        {
            StopPlayback();
            if (CurrentIndex < _images.Count)
            {
                CurrentIndex++;
            }
            SetImage();
        }

        [RelayCommand]
        private void PreImage()
        {
            StopPlayback();
            if (CurrentIndex > 1)
            {
                CurrentIndex--;
            }
            SetImage();
        }

        [RelayCommand]
        private void KeyEnter()
        {
            // 回车：选中当前照片/视频，复制到目标目录
            var fileName = SetCurrentImageFileName(_images[CurrentIndex]);
            File.Copy(_images[CurrentIndex], fileName, true);
            MessageBox.ShowAsync("已添加当前照片", "", MessageBoxIcon.Success, MessageBoxButton.OK);
        }

        [RelayCommand]
        private void PlayVideo()
        {
            // 空格键：视频/动图播放/暂停
            if (!IsVideo) return;

            if (IsPlaying && _mediaPlayer != null)
            {
                if (_mediaPlayer.IsPlaying)
                    _mediaPlayer.Pause();
                else
                    _mediaPlayer.Play();
                return;
            }

            // 开始播放
            try
            {
                if (_libVLC == null)
                    _libVLC = new LibVLC();

                StopPlayback();

                var currentFile = _images[CurrentIndex];
                var extension = System.IO.Path.GetExtension(currentFile);

                string playFile = currentFile;

                // 小米动图：需要先提取嵌入的视频数据到临时文件
                if (string.Equals(extension, ".JPG", StringComparison.OrdinalIgnoreCase))
                {
                    int videoOffset = GetEmbeddedVideoOffset(currentFile);
                    if (videoOffset >= 0)
                    {
                        byte[] fileData = File.ReadAllBytes(currentFile);
                        byte[] videoData = new byte[fileData.Length - videoOffset];
                        Array.Copy(fileData, videoOffset, videoData, 0, videoData.Length);
                        var tempMp4 = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
                        File.WriteAllBytes(tempMp4, videoData);
                        playFile = tempMp4;
                    }
                }

                var media = new Media(_libVLC, new Uri(playFile));
                _mediaPlayer = new MediaPlayer(media);
                MediaPlayer = _mediaPlayer;
                IsPlaying = true;
                _mediaPlayer.Play();
            }
            catch { }
        }

        private void StopPlayback()
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    if (_mediaPlayer.IsPlaying)
                        _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                    MediaPlayer = null;
                }
                catch { }
            }
            IsPlaying = false;
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
            SetTitle();
        }

        public void Closing()
        {
            StopPlayback();
            _libVLC?.Dispose();
            _libVLC = null;

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
