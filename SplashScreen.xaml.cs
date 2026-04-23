using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BVCC
{
    public partial class SplashScreen : System.Windows.Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            LoadingBar.IsIndeterminate = true;
        }

        private void LoadingBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LoadingBar.Template.FindName("ProgressFill", LoadingBar) is FrameworkElement fill)
            {
                double percent = LoadingBar.Value / LoadingBar.Maximum;
                double targetWidth = percent * LoadingBar.ActualWidth;
                DoubleAnimation animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
                };
                fill.BeginAnimation(FrameworkElement.WidthProperty, animation);
            }
        }
    }
}
