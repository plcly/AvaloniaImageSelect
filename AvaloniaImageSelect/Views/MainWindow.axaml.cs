using System.Collections.Generic;
using System.IO;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaImageSelect.Services;
using AvaloniaImageSelect.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Tmds.DBus.Protocol;
using Ursa.Controls;

namespace AvaloniaImageSelect.Views
{
    public partial class MainWindow : Window
    {
        
        public MainWindow()
        {
            InitializeComponent();
            ImgView.AttachedToVisualTree += (s, e) => ImgView.Focus();
        }

        private void Window_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            vm.Closing();
        }


        //protected override void OnLoaded(RoutedEventArgs e)
        //{
        //    base.OnLoaded(e);
        //    var nextAnimation = (Animation)Resources["NextImageAnimation"];
        //    var preAnimation = (Animation)Resources["PreImageAnimation"];
        //    var vm = DataContext as MainWindowViewModel;
        //    vm.NextAnimation = nextAnimation;
        //    vm.PreAnimation = preAnimation;
        //}
    }
}