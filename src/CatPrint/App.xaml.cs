using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CatPrint
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Тему применяем ДО первого окна — без белой вспышки,
            // запуск всегда с той темой, что была выбрана.
            try
            {
                ThemeManager.Apply(AppSettings.Load().Theme);
            }
            catch { }

            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
            base.OnStartup(e);
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ShowCrash(e.Exception);
            e.Handled = true;
            Shutdown(-1);
        }

        private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                ShowCrash(ex);
        }

        private static void ShowCrash(Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ex.GetType().Name + ": " + ex.Message);
            if (ex.StackTrace != null)
            {
                string[] lines = ex.StackTrace.Split('\n');
                for (int i = 0; i < Math.Min(6, lines.Length); i++)
                    sb.AppendLine(lines[i].Trim());
            }

            try
            {
                MessageBox.Show(sb.ToString(), "CatPrint — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
}
