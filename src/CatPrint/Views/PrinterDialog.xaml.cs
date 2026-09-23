using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

using CatPrint.Printing;

namespace CatPrint.Views
{
    /// <summary>
    /// Диалог принтера: только подключение.
    /// Параметры печати — в ⚙ Настройках.
    /// </summary>
    public partial class PrinterDialog : Window
    {
        private readonly AppState _state;
        private readonly AppSettings _settings;
        private CancellationTokenSource? _scanCts;

        public PrinterDialog(AppState state, AppSettings settings)
        {
            _state = state;
            _settings = settings;
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);

            _state.Printer.StateChanged += RefreshUi;
            RefreshUi();
        }

        private void RefreshUi()
        {
            Dispatcher.Invoke(() =>
            {
                bool connected = _state.Printer.IsConnected;
                bool busy = _state.Printer.State is
                    PrinterConnectionState.Connecting or
                    PrinterConnectionState.Scanning;

                TxtStatus.Text = _state.Printer.State switch
                {
                    PrinterConnectionState.Connected =>
                        $"● Подключено: {_state.Printer.DeviceName}",
                    PrinterConnectionState.Connecting => "● Подключение...",
                    PrinterConnectionState.Scanning => "● Сканирование...",
                    PrinterConnectionState.Error => "● Ошибка",
                    _ => "● Не подключено"
                };

                BtnScan.IsEnabled = !busy;
                BtnConnect.IsEnabled = !connected && !busy;
                BtnQuick.IsEnabled = !connected && !busy;
                BtnDisconnect.IsEnabled = connected;
            });
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();

            try
            {
                _state.Log("Сканирование BLE (6 c)...");
                RefreshUi();
                List<BleDeviceInfo> devices = await _state.Printer.ScanAsync(
                    TimeSpan.FromSeconds(6), _scanCts.Token);

                LstDevices.ItemsSource = devices;
                if (devices.Count > 0)
                    LstDevices.SelectedIndex = 0;

                _state.Log(devices.Count == 0
                    ? "Устройства не найдены."
                    : $"Найдено: {devices.Count}.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _state.Log($"Ошибка сканирования: {ex.Message}");
            }
            finally
            {
                RefreshUi();
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (LstDevices.SelectedItem is not BleDeviceInfo device)
            {
                MessageBox.Show("Выберите устройство из списка.",
                    "Подключение", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await DoConnect(() => _state.Printer.ConnectAsync(device), device);
        }

        private async void BtnQuick_Click(object sender, RoutedEventArgs e) =>
            await DoConnect(() => _state.Printer.ConnectMxw01Async(), null);

        private async System.Threading.Tasks.Task DoConnect(
            Func<System.Threading.Tasks.Task> action,
            BleDeviceInfo? device)
        {
            try
            {
                _state.Log("Подключение...");
                RefreshUi();
                await action();

                // Запоминаем для автоподключения
                if (device != null)
                {
                    _settings.LastDeviceAddress = device.Address;
                    _settings.LastDeviceName = device.Name;
                }
                else
                {
                    _settings.LastDeviceName = _state.Printer.DeviceName;
                }
                _settings.Save();

                _state.Log($"Подключено: {_state.Printer.DeviceName}");
            }
            catch (Exception ex)
            {
                _state.Log($"Ошибка подключения: {ex.Message}");
                MessageBox.Show(ex.Message, "Подключение",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshUi();
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _state.Printer.Disconnect();
            _state.Log("Отключено.");
            RefreshUi();
        }

        /// <summary>
        /// Запрос статуса A1 + 4 секунды слушаем ответ AE02.
        /// Батарея — байт 9 (при 85% там 0x55).
        /// </summary>
        private async void BtnStatus_Click(object sender, RoutedEventArgs e)
        {
            if (!_state.Printer.IsConnected)
            {
                MessageBox.Show("Сначала подключите принтер.",
                    "Статус", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnStatus.IsEnabled = false;
            try
            {
                _state.Log("Запрос статуса A1, слушаю AE02...");
                int count = 0;

                int? battery = await _state.Printer.RequestBatteryAsync(
                    data =>
                    {
                        count++;
                        _state.Log(
                            $"AE02 [{data.Length}]: " +
                            $"{BitConverter.ToString(data).Replace("-", " ")}");
                    });

                _state.Log(count == 0
                    ? "AE02 молчит — на A1 не отвечает."
                    : battery.HasValue
                        ? $"AE02: пакетов: {count}. Батарея: {battery}%."
                        : $"AE02: пакетов: {count}. Батарея не распознана.");
            }
            catch (Exception ex)
            {
                _state.Log($"Статус не удался: {ex.Message}");
            }
            finally
            {
                BtnStatus.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _scanCts?.Cancel();
            _state.Printer.StateChanged -= RefreshUi;
            base.OnClosed(e);
        }
    }
}
