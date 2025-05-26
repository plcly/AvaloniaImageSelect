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
using Ursa.Controls;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly SqliteService _service;
        private Dictionary<int, string> _images = new();
        private int _currentIndex = 1;
        public List<Bitmap> ImageList { get; set; }

        //public Animation NextAnimation { get; set; }
        //public Animation PreAnimation { get; set; }

        public MainWindowViewModel()
        {
            _service = App.Provider.GetRequiredService<SqliteService>();
            var folder = _service.GetImageFolder();
            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*.jpg");
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
        }

        [ObservableProperty]
        private Bitmap _currentImage;


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
    }
}
