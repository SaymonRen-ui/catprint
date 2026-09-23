using System.Windows;

namespace CatPrint.Views
{
    /// <summary>
    /// Вопрос Да/Нет в стиле приложения (вместо белого MessageBox).
    /// </summary>
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow()
        {
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);
        }

        public static bool Ask(Window owner, string title, string message)
        {
            var dlg = new ConfirmWindow
            {
                Owner = owner,
                Title = title
            };
            dlg.TxtMessage.Text = message;
            return dlg.ShowDialog() == true;
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
