using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CatPrint.Imaging
{
    /// <summary>
    /// Порядок бит внутри байта строки.
    /// LsbFirst: пиксель x — бит (x%8), т.е. 1 &lt;&lt; (x%8).
    ///   Именно так делал рабочий PrinCatTest, и MXW01, судя по печати, ждёт это.
    /// MsbFirst: пиксель x — бит 0x80 >> (x%8) (стандарт многих принтеров).
    /// </summary>
    public enum PrintBitOrder
    {
        LsbFirst,
        MsbFirst
    }

    public static class RasterConverter
    {
        public const int PrinterWidth = 384;

        public static byte[] Pack(bool[,] black, int width, int height, PrintBitOrder order)
        {
            int stride = (width + 7) / 8;
            byte[] data = new byte[stride * height];

            bool msb = order == PrintBitOrder.MsbFirst;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (!black[y, x])
                        continue;

                    if (msb)
                        data[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
                    else
                        data[y * stride + x / 8] |= (byte)(1 << (x % 8));
                }

            return data;
        }

        public static BitmapSource ToPreview(bool[,] black, int width, int height)
        {
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    byte v = black[y, x] ? (byte)0 : (byte)255;
                    int i = y * stride + x * 4;
                    pixels[i] = v;
                    pixels[i + 1] = v;
                    pixels[i + 2] = v;
                    pixels[i + 3] = 255;
                }

            var bmp = BitmapSource.Create(
                width, height, 96, 96,
                PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
    }
}
