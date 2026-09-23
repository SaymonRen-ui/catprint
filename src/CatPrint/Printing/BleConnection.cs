using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace CatPrint.Printing
{
    /// <summary>
    /// BLE-соединение с термопринтером MXW01 (семейство V5X).
    /// Повторяет рабочий PrinCat: сервис AE30, AE01 — команды,
    /// AE02 — notify, AE03 — растр. Строка растра (48 байт) пишется
    /// ОДНИМ вызовом — дробить на куски 20+20+8 НЕЛЬЗЯ, прошивка
    /// собирает строку по ATT-записи и рассинхронизируется.
    /// </summary>
    public sealed class BleConnection : IDisposable
    {
        private static readonly Guid V5XServiceUuid =
            Guid.Parse("0000ae30-0000-1000-8000-00805f9b34fb");

        private static readonly Guid Ae01Uuid =
            Guid.Parse("0000ae01-0000-1000-8000-00805f9b34fb");

        private static readonly Guid Ae02Uuid =
            Guid.Parse("0000ae02-0000-1000-8000-00805f9b34fb");

        private static readonly Guid Ae03Uuid =
            Guid.Parse("0000ae03-0000-1000-8000-00805f9b34fb");

        private const int WriteRetries = 3;

        private BluetoothLEDevice? _device;
        private GattDeviceService? _service;
        private GattCharacteristic? _ae01;
        private GattCharacteristic? _ae02;
        private GattCharacteristic? _ae03;
        private bool _notifyEnabled;

        public bool IsConnected =>
            _device != null &&
            _ae01 != null &&
            _ae02 != null &&
            _ae03 != null &&
            _notifyEnabled;

        public string DeviceNameConnected =>
            _device?.Name ?? string.Empty;

        // ============================================================
        // SCAN — список всех BLE-устройств рядом
        // ============================================================

        public static async Task<List<BleDeviceInfo>> ScanAsync(
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            var found = new Dictionary<ulong, BleDeviceInfo>();

            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            void Stop()
            {
                try { watcher.Stop(); } catch { }
            }

            watcher.Received += (sender, args) =>
            {
                try
                {
                    string name = args.Advertisement.LocalName ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                        name = "(без имени)";

                    lock (found)
                    {
                        if (found.TryGetValue(args.BluetoothAddress, out var existing))
                        {
                            existing.Rssi = args.RawSignalStrengthInDBm;
                            if (existing.Name.StartsWith("(без имени)")
                                && !name.StartsWith("(без имени)"))
                            {
                                found[args.BluetoothAddress] =
                                    new BleDeviceInfo(name, args.BluetoothAddress, args.RawSignalStrengthInDBm);
                            }
                        }
                        else
                        {
                            found[args.BluetoothAddress] =
                                new BleDeviceInfo(name, args.BluetoothAddress, args.RawSignalStrengthInDBm);
                        }
                    }
                }
                catch { }
            };

            watcher.Stopped += (sender, args) =>
            {
                tcs.TrySetResult(true);
            };

            using var _ = cancellationToken.Register(() =>
            {
                Stop();
                tcs.TrySetCanceled(cancellationToken);
            });

            watcher.Start();

            try
            {
                await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(duration, cancellationToken));
            }
            finally
            {
                Stop();
            }

            lock (found)
            {
                // MXW01 и_cat-принтеры вверх, затем по RSSI
                return found.Values
                    .OrderByDescending(d =>
                        d.Name.Contains("MXW01", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(d => d.Rssi)
                    .ToList();
            }
        }

        // ============================================================
        // CONNECT — по адресу (выбор из списка)
        // ============================================================

        public async Task ConnectAsync(
            ulong bluetoothAddress,
            CancellationToken cancellationToken = default)
        {
            Disconnect();

            BluetoothLEDevice device =
                await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);

            if (device == null)
                throw new InvalidOperationException("Не удалось открыть BLE-устройство.");

            _device = device;
            await DiscoverCharacteristicsAsync(cancellationToken);
        }

        // ============================================================
        // CONNECT — быстрый поиск MXW01 (как в старом проекте)
        // ============================================================

        public async Task ConnectToMxw01Async(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Disconnect();

            BluetoothLEDevice? device = await FindMxw01Async(
                timeout ?? TimeSpan.FromSeconds(10),
                cancellationToken);

            if (device == null)
                throw new InvalidOperationException("Принтер MXW01 не найден. Проверьте питание и Bluetooth.");

            _device = device;
            await DiscoverCharacteristicsAsync(cancellationToken);
        }

        private async Task DiscoverCharacteristicsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var servicesResult = await _device!
                .GetGattServicesAsync(BluetoothCacheMode.Uncached);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                Disconnect();
                throw new InvalidOperationException(
                    $"Не удалось получить GATT-сервисы: {servicesResult.Status}");
            }

            // Сервис V5X AE30 — строго как в рабочем проекте
            GattDeviceService? v5x = null;

            foreach (var service in servicesResult.Services)
            {
                if (service.Uuid == V5XServiceUuid)
                {
                    v5x = service;
                    break;
                }
            }

            if (v5x == null)
            {
                Disconnect();
                throw new InvalidOperationException(
                    "Сервис V5X AE30 не найден.");
            }

            _service = v5x;

            var charsResult = await v5x.GetCharacteristicsAsync(
                BluetoothCacheMode.Uncached);

            if (charsResult.Status != GattCommunicationStatus.Success)
            {
                Disconnect();
                throw new InvalidOperationException(
                    $"Не удалось получить характеристики AE30: {charsResult.Status}");
            }

            foreach (var ch in charsResult.Characteristics)
            {
                if (ch.Uuid == Ae01Uuid)
                    _ae01 = ch;
                else if (ch.Uuid == Ae02Uuid)
                    _ae02 = ch;
                else if (ch.Uuid == Ae03Uuid)
                    _ae03 = ch;
            }

            if (_ae01 == null)
            {
                Disconnect();
                throw new InvalidOperationException("Характеристика AE01 (команды) не найдена.");
            }

            if (_ae02 == null)
            {
                Disconnect();
                throw new InvalidOperationException("Характеристика AE02 (уведомления) не найдена.");
            }

            if (_ae03 == null)
            {
                Disconnect();
                throw new InvalidOperationException("Характеристика AE03 (растр) не найдена.");
            }

            // Включаем Notify на AE02 — как в рабочем проекте
            var notifyStatus = await _ae02.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            if (notifyStatus != GattCommunicationStatus.Success)
            {
                Disconnect();
                throw new InvalidOperationException(
                    $"Не удалось включить AE02 Notify: {notifyStatus}");
            }

            _notifyEnabled = true;
        }

        private static Task<BluetoothLEDevice?> FindMxw01Async(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<BluetoothLEDevice?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            void Stop()
            {
                try { watcher.Stop(); } catch { }
            }

            watcher.Received += async (sender, args) =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Stop();
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    string name = args.Advertisement.LocalName ?? string.Empty;
                    if (!string.Equals(name, "MXW01", StringComparison.OrdinalIgnoreCase))
                        return;

                    Stop();

                    var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(
                        args.BluetoothAddress);

                    tcs.TrySetResult(dev);
                }
                catch (Exception ex)
                {
                    Stop();
                    tcs.TrySetException(ex);
                }
            };

            cancellationToken.Register(() =>
            {
                Stop();
                tcs.TrySetCanceled(cancellationToken);
            });

            watcher.Start();

            _ = Task.Delay(timeout, cancellationToken).ContinueWith(_ =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    Stop();
                    tcs.TrySetResult(null);
                }
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        // ============================================================
        // WRITE — строго целиком за один вызов.
        // Рабочий проект пишет строку растра (48 байт) одним
        // WriteValueAsync; дробить вручную на 20+20+8 нельзя.
        // ============================================================

        public Task WriteAe01Async(byte[] data, CancellationToken ct = default)
        {
            if (_ae01 == null)
                throw new InvalidOperationException("AE01 недоступна.");
            return WriteCharacteristicAsync(_ae01, data, "AE01", ct);
        }

        public Task WriteAe03Async(byte[] data, CancellationToken ct = default)
        {
            if (_ae03 == null)
                throw new InvalidOperationException("AE03 недоступна.");
            return WriteCharacteristicAsync(_ae03, data, "AE03", ct);
        }

        /// <summary>
        /// Слушает уведомления AE02 указанное время (ответы принтера).
        /// </summary>
        public async Task ListenAe02Async(
            TimeSpan duration,
            Action<byte[]> onPacket,
            CancellationToken cancellationToken = default)
        {
            var ch = _ae02;
            if (ch == null || !IsConnected)
                throw new InvalidOperationException("AE02 недоступна.");

            void Handler(GattCharacteristic sender, GattValueChangedEventArgs args)
            {
                try
                {
                    using var reader = DataReader.FromBuffer(args.CharacteristicValue);
                    byte[] data = new byte[reader.UnconsumedBufferLength];
                    reader.ReadBytes(data);
                    if (data.Length > 0)
                        onPacket(data);
                }
                catch { }
            }

            ch.ValueChanged += Handler;
            try
            {
                await Task.Delay(duration, cancellationToken);
            }
            finally
            {
                ch.ValueChanged -= Handler;
            }
        }

        private static async Task WriteCharacteristicAsync(
            GattCharacteristic characteristic,
            byte[] data,
            string tag,
            CancellationToken cancellationToken)
        {
            if (data == null || data.Length == 0)
                return;

            // Ретраи: дешёвый принтер иногда глотает пакеты
            GattCommunicationStatus? status = null;
            Exception? lastError = null;

            for (int attempt = 0; attempt < WriteRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var writer = new DataWriter();
                    writer.WriteBytes(data);
                    IBuffer buffer = writer.DetachBuffer();

                    status = await characteristic.WriteValueAsync(
                        buffer,
                        GattWriteOption.WriteWithoutResponse);

                    if (status == GattCommunicationStatus.Success)
                    {
                        lastError = null;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    status = null;
                }

                await Task.Delay(30, cancellationToken);
            }

            if (status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException(
                    lastError != null
                        ? $"Принтер перестал отвечать ({tag}, {data.Length} байт): {lastError.Message}"
                        : $"Ошибка BLE-записи ({tag}): {status}");
            }
        }

        public void Disconnect()
        {
            _ae01 = null;
            _ae02 = null;
            _ae03 = null;
            _notifyEnabled = false;

            if (_service != null)
            {
                try { _service.Dispose(); } catch { }
                _service = null;
            }

            if (_device != null)
            {
                _device.Dispose();
                _device = null;
            }
        }

        public void Dispose() => Disconnect();
    }
}
