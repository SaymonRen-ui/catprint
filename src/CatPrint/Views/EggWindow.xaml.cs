using System.Windows;

namespace CatPrint.Views
{
    /// <summary>
    /// Пасхалка по клику на кота в шапке.
    /// </summary>
    public partial class EggWindow : Window
    {
        public EggWindow()
        {
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
