namespace CatPrint.Printing
{
    /// <summary>
    /// Найденное BLE-устройство (реклама).
    /// </summary>
    public sealed class BleDeviceInfo
    {
        public string Name { get; }
        public ulong Address { get; }
        public short Rssi { get; set; }

        public BleDeviceInfo(string name, ulong address, short rssi)
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"(без имени {address:X})" : name;
            Address = address;
            Rssi = rssi;
        }

        public string Display => $"{Name}  ·  {Address:X}  ·  {Rssi} dBm";

        public override string ToString() => Display;
    }
}
