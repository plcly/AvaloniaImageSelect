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
            DestinationImageFolder = _service.GetDestinationImageFolder();
            DeleteWhenClose = _service.GetDeleteWhenClose();
        }

        [ObservableProperty]
        private string _imageFolder;
        
        [ObservableProperty]
        private string _destinationImageFolder;
        
        [ObservableProperty]
        private bool _deleteWhenClose;

        [RelayCommand]
        private void PathSave()
        {
            var setting = new DbSetting
            {
                ConfigName = "ImageFolder",
                ConfigValue = ImageFolder,
                Comment = "读取图片文件夹路径"
            };
            _service.InsertOrUpdateSetting(setting);
            var settingDestinationImageFolder = new DbSetting
            {
                ConfigName = "DestinationImageFolder",
                ConfigValue = DestinationImageFolder,
                Comment = "目标图片文件夹路径"
            };
            _service.InsertOrUpdateSetting(settingDestinationImageFolder);

            var deleteWhenClose = new DbSetting
            {
                ConfigName = "DeleteWhenClose",
                ConfigValue = DeleteWhenClose.ToString(),
                Comment = "关闭窗口是否删除文件"
            };
            _service.InsertOrUpdateSetting(deleteWhenClose);
        }
    }
}
