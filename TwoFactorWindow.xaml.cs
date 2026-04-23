using System.Windows;

namespace BVCC
{
    public partial class TwoFactorWindow : Window
    {
        public string Code { get; private set; }

        public TwoFactorWindow()
        {
            InitializeComponent();
            CodeBox.Focus();
        }

        private void Verify_Click(object sender, RoutedEventArgs e)
        {
            Code = CodeBox.Text;
            DialogResult = true;
            Close();
        }

        private void CodeBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {

        }
    }
}