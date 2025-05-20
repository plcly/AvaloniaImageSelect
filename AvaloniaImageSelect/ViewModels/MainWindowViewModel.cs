using Avalonia.Controls;
using AvaloniaImageSelect.Views;
using CommunityToolkit.Mvvm.Input;
using Ursa.Controls;

namespace AvaloniaImageSelect.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public string Greeting { get; } = "Welcome to Avalonia!";

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
    }
}
