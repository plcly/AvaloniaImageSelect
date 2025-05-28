using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using AvaloniaImageSelect.Services;
using AvaloniaImageSelect.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic.FileIO;
using Ursa.Controls;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SqliteService _service;
        private string _imageFolder;
        private string _imageDestinationFolder;
        private Dictionary<int, string> _images = new();
        private int _currentIndex = 1;
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
                    var files = Directory.GetFiles(_imageFolder, "*.JPG");
                    if (files.Length > 0)
                    {
                        for (int i = 0; i < files.Length; i++)
                        {
                            _images.Add(i + 1, files[i]);
                        }
                    }
                }
                CurrentImage = new Bitmap(_images[1]);
                SetTitle();
                PrefixDate = DateTime.Now.ToString("yyyyMMdd");
            }
        }

        [ObservableProperty]
        private Bitmap _currentImage;
        
        [ObservableProperty]
        private string _prefixDate;


        [ObservableProperty]
        private string _title;

        public void SetTitle()
        {
            if (_images.Count > 0 && _images.ContainsKey(_currentIndex))
            {
                Title = $"{_images[_currentIndex]}({_currentIndex}/{_images.Count})";
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
            if (_currentIndex < _images.Count)
            {
                _currentIndex++;
            }

            CurrentImage = new Bitmap(_images[_currentIndex]);
            GC.Collect();
            //NextAnimation.RunAsync(image);
            SetTitle();
        }

        [RelayCommand]
        private void PreImage(ImageViewer image)
        {
            if (_currentIndex > 1)
            {
                _currentIndex--;
            }

            CurrentImage = new Bitmap(_images[_currentIndex]);
            GC.Collect();
            //PreAnimation.RunAsync(image);
            SetTitle();
        }
        
        [RelayCommand]
        private void KeyEnter()
        {
            var fileName = SetCurrentImageFileName();
            File.Copy(_images[_currentIndex],fileName, true);
            MessageBox.ShowAsync("已添加当前照片", "", MessageBoxIcon.Success, MessageBoxButton.OK);
        }

        private string SetCurrentImageFileName()
        {
            var files = Directory.GetFiles(_imageFolder, _prefixDate + "*.JPG", System.IO.SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                return System.IO.Path.Combine(_imageFolder, _prefixDate + ".JPG");
            }
            return System.IO.Path.Combine(_imageFolder, _prefixDate + "-" + (files.Length) + ".JPG");
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
