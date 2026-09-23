using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

using Microsoft.Win32;

using CatPrint.Views;

namespace CatPrint
{
    public partial class MainWindow : Window
    {
        private readonly AppState _state = new();
        private readonly AppSettings _settings = AppSettings.Load();
        private CancellationTokenSource? _printCts;
        private string? _docPath;

        public MainWindow()
        {
            InitializeComponent();

            Views.DarkTitleBar.Apply(this, _settings.Theme == AppTheme.Dark);

            Editor.Settings = _settings;
            RefreshPowerPopup();

            ThemeManager.Apply(_settings.Theme);
            UpdateThemeButton();

            _state.LogMessage += text =>
                Dispatcher.Invoke(() => TxtStatus.Text = text);

            _state.Printer.StateChanged += () =>
                Dispatcher.Invoke(RefreshPrinterPill);

            Editor.DocumentChanged += () =>
                Dispatcher.Invoke(RefreshPrintButton);

            RefreshPrinterPill();
            RefreshPrintButton();

            Title = "CatPrint — новый документ";
            _state.Log("Готов. Принтер — кнопка справа вверху.");

            // Батарея медленно садится: опрос при коннекте,
            // после печати и раз в 10 минут
            _batteryTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(10)
            };
            _batteryTimer.Tick += async (s, e) => await QueryBatteryAsync();
            _batteryTimer.Start();

            // Автоподключение к последнему устройству
            _ = AutoConnectAsync();
        }

        private readonly System.Windows.Threading.DispatcherTimer _batteryTimer;

        private async System.Threading.Tasks.Task QueryBatteryAsync()
        {
            if (!_state.Printer.IsConnected)
                return;

            try
            {
                int? level = await _state.Printer.RequestBatteryAsync();
                Dispatcher.Invoke(() =>
                {
                    if (level.HasValue)
                    {
                        TxtBattery.Text = $"🔋{level}%";
                        TxtBattery.ToolTip = $"Батарея принтера: {level}%";
                        TxtBattery.Visibility = Visibility.Visible;
                        _state.Log($"Батарея принтера: {level}%.");
                    }
                    else
                    {
                        TxtBattery.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch { }
        }

        // ============================================================
        // ТЕМА
        // ============================================================

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            _settings.Theme =
                _settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            _settings.Save();
            ThemeManager.Apply(_settings.Theme);
            UpdateThemeButton();
            Editor.RedrawRuler();
        }

        private void UpdateThemeButton() =>
            BtnTheme.Content = _settings.Theme == AppTheme.Dark ? "☀" : "🌙";

        // ============================================================
        // ПЛОТНОСТЬ: одна кнопка в шапке + всплывашка.
        // При включённой автоплотности слайдер мёртвый (жар решает сам).
        // ============================================================

        private void RefreshPowerPopup()
        {
            if (PowSlider == null)
                return;
            PowSlider.Value = Views.DocEditorView.PowerByteToSlider(_settings.Intensity);
            PowTitle.Text = "Плотность: " + Views.DocEditorView.PowerDisplay(PowSlider.Value);
            PowAuto.IsChecked = _settings.AutoDensity;
            PowAdaptive.IsChecked = _settings.AdaptiveHeat;
            UpdatePowerEnabled();
            int pct = (int)Math.Round(Math.Clamp(PowSlider.Value, 0, 100));
            TxtPowerBtn.Text = $"{pct}%";
        }

        /// <summary>
        /// При Авто слайдер мёртвый: блокируем и сереем явно —
        /// кастомный стиль слайдера в темах disabled-состояния не рисует.
        /// </summary>
        private void UpdatePowerEnabled()
        {
            bool auto = PowAuto.IsChecked == true;
            PowSlider.IsEnabled = !auto;
            PowSlider.Opacity = auto ? 0.4 : 1.0;
            PowTitle.Opacity = auto ? 0.55 : 1.0;
        }

        private void BtnPower_Click(object sender, RoutedEventArgs e)
        {
            RefreshPowerPopup();
            PowerPopup.IsOpen = true;
        }

        private void PowSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PowTitle == null)
                return;
            _settings.Intensity = Views.DocEditorView.PowerSliderToByte(e.NewValue);
            PowTitle.Text = "Плотность: " + Views.DocEditorView.PowerDisplay(e.NewValue);
            int pct = (int)Math.Round(Math.Clamp(e.NewValue, 0, 100));
            TxtPowerBtn.Text = $"{pct}%";
            _settings.Save();
        }

        private void PowAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (PowSlider == null)
                return;
            if (sender == PowAuto)
                _settings.AutoDensity = PowAuto.IsChecked == true;
            else if (sender == PowAdaptive)
                _settings.AdaptiveHeat = PowAdaptive.IsChecked == true;
            UpdatePowerEnabled();
            _settings.Save();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_settings) { Owner = this };
            dlg.ShowDialog();
            UpdateThemeButton();
            RefreshPowerPopup();
            Editor.RedrawRuler();
        }

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new LogWindow(_state) { Owner = this };
            dlg.ShowDialog();
        }

        // Пасхалка: клик по коту в шапке
        private void BtnLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EggWindow { Owner = this };
            dlg.ShowDialog();
        }

        // ============================================================
        // ПРИНТЕР: ПИЛЮЛЯ + АВТОПОДКЛЮЧЕНИЕ
        // ============================================================

        private void RefreshPrinterPill()
        {
            bool connected = _state.Printer.IsConnected;

            TxtPrinter.Text = _state.Printer.State switch
            {
                Printing.PrinterConnectionState.Connected =>
                    _state.Printer.DeviceName,
                Printing.PrinterConnectionState.Connecting => "Подключение...",
                Printing.PrinterConnectionState.Scanning => "Сканирование...",
                _ when _settings.LastDeviceAddress.HasValue =>
                    $"○ {_settings.LastDeviceName}",
                _ => "Не подключено"
            };

            PrinterDot.Fill = connected
                ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
                : Brushes.Gray;

            if (connected && !_wasConnected)
            {
                _ = QueryBatteryAsync();
            }
            else if (!connected)
            {
                TxtBattery.Visibility = Visibility.Collapsed;
            }
            _wasConnected = connected;

            RefreshPrintButton();
        }

        private bool _wasConnected;

        private void BtnPrinter_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PrinterDialog(_state, _settings)
            {
                Owner = this
            };
            dlg.ShowDialog();
            RefreshPowerPopup();
            RefreshPrinterPill();
        }

        private async System.Threading.Tasks.Task AutoConnectAsync()
        {
            if (!_settings.LastDeviceAddress.HasValue)
                return;

            await System.Threading.Tasks.Task.Delay(1500);

            try
            {
                _state.Log($"Автоподключение к {_settings.LastDeviceName}...");
                await _state.Printer.ConnectAsync(
                    new Printing.BleDeviceInfo(
                        _settings.LastDeviceName,
                        _settings.LastDeviceAddress.Value, 0));
                _state.Log($"Подключено: {_state.Printer.DeviceName}");
            }
            catch (Exception ex)
            {
                _state.Log($"Автоподключение не удалось: {ex.Message}");
            }
        }

        // ============================================================
        // ДОКУМЕНТ
        // ============================================================

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            Editor.NewDocument();
            _docPath = null;
            Title = "CatPrint — новый документ";
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Открыть документ",
                Filter = "CatPrint|*.catdoc|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                Editor.LoadFrom(dlg.FileName);
                _docPath = dlg.FileName;
                Title = $"CatPrint — {System.IO.Path.GetFileName(_docPath)}";
                _state.Log($"Открыто: {System.IO.Path.GetFileName(_docPath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Открыть",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_docPath == null)
                {
                    var dlg = new SaveFileDialog
                    {
                        Title = "Сохранить документ",
                        Filter = "CatPrint|*.catdoc",
                        DefaultExt = ".catdoc"
                    };
                    if (dlg.ShowDialog() != true)
                        return;
                    _docPath = dlg.FileName;
                }

                Editor.SaveTo(_docPath);
                Title = $"CatPrint — {System.IO.Path.GetFileName(_docPath)}";
                _state.Log("Документ сохранён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Сохранить",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (!Editor.HasBlocks)
                return;

            if (!Views.ConfirmWindow.Ask(this, "Очистить",
                    "Очистить холст? Документ станет пустым."))
                return;

            Editor.NewDocument();
            _docPath = null;
            Title = "CatPrint — новый документ";
            _state.Log("Холст очищен.");
        }

        // ============================================================
        // ПЕЧАТЬ
        // ============================================================

        private void RefreshPrintButton()
        {
            if (BtnPrint == null)
                return;

            BtnPrint.IsEnabled =
                _state.Printer.IsConnected &&
                Editor.HasBlocks &&
                _printCts == null;
        }

        private async void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (!_state.Printer.IsConnected || !Editor.HasBlocks)
                return;

            _printCts = new CancellationTokenSource();
            RefreshPrintButton();
            PrintProgress.Value = 0;

            try
            {
                var img = Editor.BuildPrintImage(_settings.BitOrder);
                _state.Log($"Печать: 384×{img.Height} (~{img.HeightMm:0} мм)...");

                var progress = new Progress<double>(v =>
                    PrintProgress.Value = v);

                await _state.PrintService.PrintAsync(
                    img.Raster, img.Width, img.Height,
                    _settings.Intensity, progress, _printCts.Token,
                    _settings.LineDelayMs,
                    _settings.BlockLines,
                    _settings.BlockPauseMs,
                    msg => _state.Log(msg),
                    _settings.SplitEnabled,
                    _settings.SplitLines,
                    _settings.SplitPauseMs,
                    _settings.AutoDensity,
                    _settings.AdaptiveHeat);

                _state.Log("Напечатано.");
                PrintProgress.Value = 0;
                _ = QueryBatteryAsync();
            }
            catch (OperationCanceledException)
            {
                _state.Log("Печать отменена.");
            }
            catch (Exception ex)
            {
                _state.Log($"Ошибка печати: {ex.Message}");
                MessageBox.Show(ex.Message, "Печать",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _printCts?.Dispose();
                _printCts = null;
                RefreshPrintButton();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _batteryTimer.Stop();
                _printCts?.Cancel();
                _state.Printer.Dispose();
            }
            catch { }

            base.OnClosed(e);
        }
    }
}
