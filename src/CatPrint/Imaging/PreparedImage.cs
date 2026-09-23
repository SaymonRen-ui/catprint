using System.Windows.Media.Imaging;

namespace CatPrint.Imaging
{
    /// <summary>
    /// Готовое изображение для печати: превью + растр 384px, MSB first.
    /// </summary>
    public sealed class PreparedImage
    {
        public BitmapSource Preview { get; }
        public byte[] Raster { get; }
        public int Width { get; }
        public int Height { get; }

        public PreparedImage(BitmapSource preview, byte[] raster, int width, int height)
        {
            Preview = preview;
            Raster = raster;
            Width = width;
            Height = height;
        }

        public double HeightMm => Height / 8.0; // ~203 dpi ≈ 8 точек/мм
    }

    public sealed class ImageOptions
    {
        /// <summary>-100..100</summary>
        public double Brightness { get; set; } = 0;
        /// <summary>-100..100</summary>
        public double Contrast { get; set; } = 0;
        /// <summary>0..200, 100 = нормально</summary>
        public double Saturation { get; set; } = 100;
        public DitherMode Dither { get; set; } = DitherMode.FloydSteinberg;
        /// <summary>0..255, только для Threshold</summary>
        public int Threshold { get; set; } = 128;
        public bool Invert { get; set; } = false;
        public PrintBitOrder BitOrder { get; set; } = PrintBitOrder.LsbFirst;
        /// <summary>Гамма 0.2..3.0, 1.0 = без изменений</summary>
        public double Gamma { get; set; } = 1.0;
    }
}
