using System;

namespace CatPrint.Printing
{
    /// <summary>
    /// Построение команд MXW01. Формат из рабочего PrinCatTest:
    /// 22 21 CMD 00 LEN_LO LEN_HI PAYLOAD CRC  FF
    /// CRC8 (poly 0x07) — только для A2, для A9/AD хвост 00 00.
    /// </summary>
    public static class Mxw01Protocol
    {
        public const int PrinterWidth = 384;
        public const int BytesPerLine = 48;

        public const byte Header1 = 0x22;
        public const byte Header2 = 0x21;
        public const byte Terminator = 0xFF;

        public const byte CmdSetIntensity = 0xA2;
        public const byte CmdStatusRequest = 0xA1;
        public const byte CmdPrintRequest = 0xA9;
        public const byte CmdFlush = 0xAD;

        public static byte[] Intensity(byte level) =>
            CreateCommandWithCrc(CmdSetIntensity, new[] { level });

        /// <summary>
        /// Запрос статуса A1. Принтер должен ответить пакетом в AE02.
        /// Формат ответа неизвестен — разбираем по факту (там может быть батарея).
        /// </summary>
        public static byte[] StatusRequest() =>
            CreateSimpleCommand(CmdStatusRequest, new byte[] { 0x00 });

        /// <summary>
        /// Батарея из ответа на A1: байт 9 (проверено: при 85% там 0x55,
        /// единственный байт пакета, равный 85). Иначе null.
        /// </summary>
        public static int? ParseStatusBattery(byte[] packet)
        {
            if (packet == null || packet.Length < 10)
                return null;

            if (packet[0] != Header1 ||
                packet[1] != Header2 ||
                packet[2] != CmdStatusRequest)
                return null;

            int value = packet[9];
            if (value < 0 || value > 100)
                return null;

            return value;
        }

        public static byte[] PrintRequest(int height) =>            CreateSimpleCommand(CmdPrintRequest, new[]
            {
                (byte)(height & 0xFF),
                (byte)((height >> 8) & 0xFF),
                (byte)(BytesPerLine & 0xFF),
                (byte)((BytesPerLine >> 8) & 0xFF)
            });

        public static byte[] Flush() =>
            CreateSimpleCommand(CmdFlush, new byte[] { 0x00 });

        private static byte[] CreateCommandWithCrc(byte command, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            byte[] result = new byte[8 + payload.Length];

            result[0] = Header1;
            result[1] = Header2;
            result[2] = command;
            result[3] = 0x00;
            result[4] = (byte)(payload.Length & 0xFF);
            result[5] = (byte)((payload.Length >> 8) & 0xFF);

            Buffer.BlockCopy(payload, 0, result, 6, payload.Length);

            result[6 + payload.Length] = CalculateCrc8(payload);
            result[7 + payload.Length] = Terminator;

            return result;
        }

        private static byte[] CreateSimpleCommand(byte command, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            byte[] result = new byte[8 + payload.Length];

            result[0] = Header1;
            result[1] = Header2;
            result[2] = command;
            result[3] = 0x00;
            result[4] = (byte)(payload.Length & 0xFF);
            result[5] = (byte)((payload.Length >> 8) & 0xFF);

            Buffer.BlockCopy(payload, 0, result, 6, payload.Length);

            // A9 / AD — рабочий формат без CRC
            result[6 + payload.Length] = 0x00;
            result[7 + payload.Length] = 0x00;

            return result;
        }

        public static byte CalculateCrc8(byte[] data)
        {
            byte crc = 0x00;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
    }
}
