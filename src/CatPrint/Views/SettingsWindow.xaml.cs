using System;
using System.Windows;
using System.Windows.Controls;

using CatPrint.Imaging;

namespace CatPrint.Views
{
    /// <summary>
    /// Настройки: параметры печати и приложения.
    /// Применяются сразу, хранятся в %AppData%/CatPrint.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);
            RefreshFromSettings();
        }

        private void RefreshFromSettings()
        {
            SldPower.Value = DocEditorView.PowerByteToSlider(_settings.Intensity);
            RunPower.Text = DocEditorView.PowerDisplay(SldPower.Value);

            CmbBitOrder.SelectedIndex =
                _settings.BitOrder == PrintBitOrder.MsbFirst ? 1 : 0;

            SldLineDelay.Value = Math.Clamp(_settings.LineDelayMs, 5, 80);
            RunLineDelay.Text = _settings.LineDelayMs.ToString();

            SldBlockLines.Value = Math.Clamp(_settings.BlockLines, 10, 100);
            RunBlockLines.Text = _settings.BlockLines.ToString();

            SldBlockPause.Value = Math.Clamp(_settings.BlockPauseMs, 0, 2000);
            RunBlockPause.Text = _settings.BlockPauseMs.ToString();

            ChkSplit.IsChecked = _settings.SplitEnabled;
            SldSplitLines.Value = Math.Clamp(_settings.SplitLines, 60, 240);
            RunSplitLines.Text = _settings.SplitLines.ToString();
            SldSplitPause.Value = Math.Clamp(_settings.SplitPauseMs, 200, 3000);
            RunSplitPause.Text = _settings.SplitPauseMs.ToString();

            CmbTheme.SelectedIndex =
                _settings.Theme == AppTheme.Dark ? 1 : 0;
        }

        private void SldPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            _settings.Intensity = DocEditorView.PowerSliderToByte(e.NewValue);
            RunPower.Text = DocEditorView.PowerDisplay(e.NewValue);
            _settings.Save();
        }

        /// <summary>
        /// Пресеты печати: одним нажатием выставляют связку настроек.
        /// </summary>
        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            switch (btn.Tag as string)
            {
                case "doc": // Обычный документ
                    _settings.Intensity = 0x5D;
                    _settings.LineDelayMs = 15;
                    _settings.BlockLines = 40;
                    _settings.BlockPauseMs = 0;
                    _settings.SplitEnabled = false;
                    break;

                case "photo": // Фото: спокойнее и по частям
                    _settings.Intensity = 0x55;
                    _settings.LineDelayMs = 15;
                    _settings.BlockLines = 40;
                    _settings.BlockPauseMs = 0;
                    _settings.SplitEnabled = true;
                    _settings.SplitLines = 96;
                    _settings.SplitPauseMs = 1500;
                    break;

                case "fast": // Черновик
                    _settings.Intensity = 0x40;
                    _settings.LineDelayMs = 8;
                    _settings.BlockLines = 40;
                    _settings.BlockPauseMs = 0;
                    _settings.SplitEnabled = false;
                    break;

                case "safe": // Медленно и надёжно
                    _settings.Intensity = 0x5D;
                    _settings.LineDelayMs = 30;
                    _settings.BlockLines = 40;
                    _settings.BlockPauseMs = 500;
                    _settings.SplitEnabled = true;
                    _settings.SplitLines = 96;
                    _settings.SplitPauseMs = 2000;
                    break;

                default:
                    return;
            }

            _settings.Save();
            RefreshFromSettings();
        }

        private void CmbBitOrder_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbBitOrder.SelectedIndex < 0)
                return;
            _settings.BitOrder = CmbBitOrder.SelectedIndex == 1
                ? PrintBitOrder.MsbFirst
                : PrintBitOrder.LsbFirst;
            _settings.Save();
        }

        private void SldLineDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            _settings.LineDelayMs = (int)e.NewValue;
            RunLineDelay.Text = _settings.LineDelayMs.ToString();
            _settings.Save();
        }

        private void SldBlockLines_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            _settings.BlockLines = (int)e.NewValue;
            RunBlockLines.Text = _settings.BlockLines.ToString();
            _settings.Save();
        }

        private void SldBlockPause_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            _settings.BlockPauseMs = (int)e.NewValue;
            RunBlockPause.Text = _settings.BlockPauseMs.ToString();
            _settings.Save();
        }

        private void ChkSplit_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
                return;
            _settings.SplitEnabled = ChkSplit.IsChecked == true;
            _settings.Save();
        }

        private void SldSplitLines_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            _settings.SplitLines = (int)e.NewValue;
            RunSplitLines.Text = _settings.SplitLines.ToString();
            _settings.Save();
        }

        private void SldSplitPause_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            _settings.SplitPauseMs = (int)e.NewValue;
            RunSplitPause.Text = _settings.SplitPauseMs.ToString();
            _settings.Save();
        }

        private void CmbTheme_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbTheme.SelectedIndex < 0)
                return;
            _settings.Theme = CmbTheme.SelectedIndex == 1
                ? AppTheme.Dark : AppTheme.Light;
            _settings.Save();
            ThemeManager.Apply(_settings.Theme);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmWindow.Ask(this, "Сброс",
                    "Сбросить все настройки к значениям по умолчанию?"))
                return;

            var fresh = new AppSettings
            {
                // Принтер не забываем — переподключаться лень всем
                LastDeviceAddress = _settings.LastDeviceAddress,
                LastDeviceName = _settings.LastDeviceName,
                Theme = _settings.Theme
            };

            _settings.Intensity = fresh.Intensity;
            _settings.BitOrder = fresh.BitOrder;
            _settings.LineDelayMs = fresh.LineDelayMs;
            _settings.BlockLines = fresh.BlockLines;
            _settings.BlockPauseMs = fresh.BlockPauseMs;
            _settings.SplitEnabled = fresh.SplitEnabled;
            _settings.SplitLines = fresh.SplitLines;
            _settings.SplitPauseMs = fresh.SplitPauseMs;
            _settings.Save();

            RefreshFromSettings();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
