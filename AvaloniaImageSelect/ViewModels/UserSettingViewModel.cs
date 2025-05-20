using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class UserSettingViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _imagePath;

        [RelayCommand]
        private void PathSave()
        {

        }
    }
}
