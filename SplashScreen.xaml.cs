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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace BVCC
{
    public partial class SplashScreen : System.Windows.Window
    {
        private bool _isupdating = false;
        public bool IsUpdating
        {
            get
            {

                return _isupdating;
            }
            set
            {
                if(value == true)
                {
                    LoadingBar.Visibility = Visibility.Visible;
                    LoadingStatus.Visibility = Visibility.Visible;
                }
                else
                {
                    LoadingBar.Visibility = Visibility.Collapsed;
                    LoadingStatus.Visibility = Visibility.Collapsed;
                }
                _isupdating = value;
            }
        }
        public SplashScreen()
        {
            InitializeComponent();
            LoadingBar.Visibility = Visibility.Collapsed;
            LoadingStatus.Visibility = Visibility.Collapsed;
        }
    }
}
