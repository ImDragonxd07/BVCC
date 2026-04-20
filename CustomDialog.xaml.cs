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
using System.Windows.Shapes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Window = System.Windows.Window;

namespace BVCC
{
    public partial class CustomDialog : System.Windows.Window
    {
        public CustomDialog()
        {
            InitializeComponent();
        }
        public enum Mode { Message, Question, Readme }

        public static bool? Show(string body, string title = "BVCC", Mode mode = Mode.Message)
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => Show(body, title, mode));
            }

            var dialog = new CustomDialog();
            var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsVisible && x.IsActive)
                               ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsVisible);

            if (activeWindow != null)
            {
                dialog.Owner = activeWindow;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.TxtTitle.Text = title;
            dialog.Topmost = true; 

            switch (mode)
            {
                case Mode.Message:
                    dialog.TxtMessage.Text = body;
                    dialog.TxtMessage.Visibility = Visibility.Visible;
                    break;

                case Mode.Question:
                    dialog.TxtMessage.Text = body;
                    dialog.TxtMessage.Visibility = Visibility.Visible;
                    dialog.BtnNo.Visibility = Visibility.Visible;
                    dialog.BtnOk.Content = "Yes";
                    break;

                case Mode.Readme:
                    dialog.TxtLongBody.Text = body;
                    dialog.ScrollArea.Visibility = Visibility.Visible;
                    dialog.Height = 450;
                    dialog.Width = 600;
                    break;
            }
            return dialog.ShowDialog();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
