using System;
using System.Windows;

namespace CatPrint.Views
{
    /// <summary>
    /// Вся история сообщений: ошибки печати с номерами строк — здесь.
    /// </summary>
    public partial class LogWindow : Window
    {
        private readonly AppState _state;

        public LogWindow(AppState state)
        {
            _state = state;
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);

            foreach (string line in _state.GetHistory())
                LstLog.Items.Add(line);

            if (LstLog.Items.Count > 0)
                LstLog.ScrollIntoView(LstLog.Items[^1]);

            _state.LogMessage += OnLog;
        }

        private void OnLog(string line) =>
            Dispatcher.Invoke(() =>
            {
                LstLog.Items.Add(line);
                LstLog.ScrollIntoView(line);
            });

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(string.Join(
                    Environment.NewLine, _state.GetHistory()));
            }
            catch { }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _state.ClearHistory();
            LstLog.Items.Clear();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _state.LogMessage -= OnLog;
            base.OnClosed(e);
        }
    }
}
