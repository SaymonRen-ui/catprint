using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CatPrint.Printing
{
    public sealed class PrinterConnection : IDisposable
    {
        private readonly BleConnection _ble = new();

        public PrinterConnectionState State { get; private set; }
            = PrinterConnectionState.Disconnected;

        public event Action? StateChanged;

        public bool IsConnected =>
            State == PrinterConnectionState.Connected && _ble.IsConnected;

        public string DeviceName => _ble.DeviceNameConnected;

        private void SetState(PrinterConnectionState state)
        {
            State = state;
            StateChanged?.Invoke();
        }

        // Сканирование: вернуть список устройств
        public async Task<List<BleDeviceInfo>> ScanAsync(
            TimeSpan? duration = null,
            CancellationToken ct = default)
        {
            SetState(PrinterConnectionState.Scanning);
            try
            {
                var list = await BleConnection.ScanAsync(
                    duration ?? TimeSpan.FromSeconds(6), ct);
                SetState(PrinterConnectionState.Disconnected);
                return list;
            }
            catch
            {
                SetState(PrinterConnectionState.Disconnected);
                throw;
            }
        }

        public async Task ConnectAsync(
            BleDeviceInfo device,
            CancellationToken ct = default)
        {
            if (IsConnected)
                return;

            SetState(PrinterConnectionState.Connecting);
            try
            {
                await _ble.ConnectAsync(device.Address, ct);
                SetState(PrinterConnectionState.Connected);
            }
            catch
            {
                SetState(PrinterConnectionState.Error);
                throw;
            }
        }

        // Быстрое подключение к MXW01 без выбора
        public async Task ConnectMxw01Async(CancellationToken ct = default)
        {
            if (IsConnected)
                return;

            SetState(PrinterConnectionState.Connecting);
            try
            {
                await _ble.ConnectToMxw01Async(TimeSpan.FromSeconds(10), ct);
                SetState(PrinterConnectionState.Connected);
            }
            catch
            {
                SetState(PrinterConnectionState.Error);
                throw;
            }
        }

        public Task SendCommandAsync(byte[] data, CancellationToken ct = default)
        {
            EnsureConnected();
            return _ble.WriteAe01Async(data, ct);
        }

        public Task SendRasterAsync(byte[] data, CancellationToken ct = default)
        {
            EnsureConnected();
            return _ble.WriteAe03Async(data, ct);
        }

        /// <summary>Запрос статуса A1 (ответ приходит в AE02).</summary>
        public Task SendStatusRequestAsync(CancellationToken ct = default)
        {
            EnsureConnected();
            return _ble.WriteAe01Async(Mxw01Protocol.StatusRequest(), ct);
        }

        public Task ListenAe02Async(
            TimeSpan duration, Action<byte[]> onPacket, CancellationToken ct = default) =>
            _ble.ListenAe02Async(duration, onPacket, ct);

        /// <summary>
        /// Последний известный заряд (обновляется запросом батареи).
        /// </summary>
        public int? LastBattery { get; private set; }

        /// <summary>
        /// Шлёт A1, 4 секунды слушает AE02, вытаскивает батарею.
        /// onPacket — для журнала сырых пакетов (можно null).
        /// </summary>
        public async Task<int?> RequestBatteryAsync(
            Action<byte[]>? onPacket = null,
            CancellationToken ct = default)
        {
            EnsureConnected();

            int? found = null;

            var listen = _ble.ListenAe02Async(
                TimeSpan.FromSeconds(4),
                data =>
                {
                    onPacket?.Invoke(data);
                    var parsed = Mxw01Protocol.ParseStatusBattery(data);
                    if (parsed.HasValue)
                        found = parsed.Value;
                },
                ct);

            await _ble.WriteAe01Async(Mxw01Protocol.StatusRequest(), ct);
            await listen;

            if (found.HasValue)
                LastBattery = found.Value;

            return found;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Принтер не подключён.");
        }

        public void Disconnect()
        {
            _ble.Disconnect();
            LastBattery = null;
            SetState(PrinterConnectionState.Disconnected);
        }

        public void Dispose() => Disconnect();
    }
}
