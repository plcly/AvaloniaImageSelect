using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AvaloniaImageSelect.Models;
using AvaloniaImageSelect.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class UserSettingViewModel : ViewModelBase
    {
        private SqliteService _service;
        public UserSettingViewModel()
        {
            _service = App.Provider.GetRequiredService<SqliteService>();
            ImageFolder = _service.GetImageFolder();
        }

        [ObservableProperty]
        private string _imageFolder;

        [RelayCommand]
        private void PathSave()
        {
            var setting = new DbSetting
            {
                ConfigName = "ImageFolder",
                ConfigValue = ImageFolder,
                Comment = "图片存储路径"
            };
            _service.InsertOrUpdateSetting(setting);
        }
    }
}
