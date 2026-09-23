using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CatPrint.Imaging
{
    /// <summary>
    /// Пайплайн фото: resize до 384 → яркость/контраст/насыщенность → gray → дизеринг → растр.
    /// </summary>
    public sealed class PhotoPipeline
    {
        public const int PrinterWidth = 384;
        public const int MaxHeight = 2000;

        public PreparedImage Prepare(BitmapSource source, ImageOptions options)
        {
            options ??= new ImageOptions();
            var (black, w, h) = RenderBool(source, options);

            byte[] raster = RasterConverter.Pack(black, w, h, options.BitOrder);
            BitmapSource preview = RasterConverter.ToPreview(black, w, h);

            return new PreparedImage(preview, raster, w, h);
        }

        /// <summary>
        /// Только 1-битная матрица (для склейки документа и превью блоков).
        /// targetWidth — для рамок: пайплайн на внутренней ширине.
        /// </summary>
        public (bool[,] Black, int W, int H) RenderBool(
            BitmapSource source, ImageOptions options, int targetWidth = PrinterWidth)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            options ??= new ImageOptions();

            // 1. В Bgra32 для ручной обработки
            BitmapSource bgra = EnsureBgra32(source);

            // 2. Resize до целевой ширины (сохраняем пропорции)
            BitmapSource resized = ResizeToWidth(bgra, targetWidth);

            if (resized.PixelHeight > MaxHeight)
                throw new InvalidOperationException(
                    $"Изображение слишком высокое: {resized.PixelHeight}px. Максимум {MaxHeight}px.");

            int w = resized.PixelWidth;
            int h = resized.PixelHeight;

            // 3. Пиксели + adjustments
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            resized.CopyPixels(pixels, stride, 0);

            float[,] gray = new float[h, w];

            double brightnessOffset = options.Brightness * 2.55; // -255..255
            double contrastC = options.Contrast * 2.55;
            double contrastFactor = (259.0 * (contrastC + 255.0)) / (255.0 * (259.0 - contrastC));
            double sat = Math.Clamp(options.Saturation / 100.0, 0.0, 2.0);
            double gamma = Math.Clamp(options.Gamma, 0.2, 3.0);
            bool useGamma = Math.Abs(gamma - 1.0) > 0.001;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    double b = pixels[i];
                    double g = pixels[i + 1];
                    double r = pixels[i + 2];

                    // Насыщенность
                    if (Math.Abs(sat - 1.0) > 0.001)
                    {
                        double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                        r = lum + (r - lum) * sat;
                        g = lum + (g - lum) * sat;
                        b = lum + (b - lum) * sat;
                    }

                    // Яркость + контраст
                    r = (r - 128.0) * contrastFactor + 128.0 + brightnessOffset;
                    g = (g - 128.0) * contrastFactor + 128.0 + brightnessOffset;
                    b = (b - 128.0) * contrastFactor + 128.0 + brightnessOffset;

                    // Гамма
                    if (useGamma)
                    {
                        r = 255.0 * Math.Pow(Math.Clamp(r, 0, 255) / 255.0, gamma);
                        g = 255.0 * Math.Pow(Math.Clamp(g, 0, 255) / 255.0, gamma);
                        b = 255.0 * Math.Pow(Math.Clamp(b, 0, 255) / 255.0, gamma);
                    }

                    r = Math.Clamp(r, 0, 255);
                    g = Math.Clamp(g, 0, 255);
                    b = Math.Clamp(b, 0, 255);

                    gray[y, x] = (float)(0.299 * r + 0.587 * g + 0.114 * b);
                }
            }

            // 4. Дизеринг
            bool[,] black = Dithering.Apply(gray, w, h, options.Dither, options.Threshold);

            if (options.Invert)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        black[y, x] = !black[y, x];
            }

            return (black, w, h);
        }

        private static BitmapSource EnsureBgra32(BitmapSource source)
        {
            if (source.Format == PixelFormats.Bgra32)
            {
                source.Freeze();
                return source;
            }

            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            return converted;
        }

        private static BitmapSource ResizeToWidth(BitmapSource source, int targetWidth)
        {
            if (source.PixelWidth == targetWidth)
                return source;

            // Пошаговое уменьшение вдвое: одношаговый даунскейл
            // большого фото даёт муар (ложные круги и сетки),
            // которые потом видны при любом дизеринге.
            BitmapSource current = source;
            while (current.PixelWidth > targetWidth * 2)
            {
                var half = new TransformedBitmap();
                half.BeginInit();
                half.Source = current;
                half.Transform = new ScaleTransform(0.5, 0.5);
                half.EndInit();
                half.Freeze();
                current = half;
            }

            if (current.PixelWidth == targetWidth)
                return current;

            double scale = (double)targetWidth / current.PixelWidth;
            var transformed = new TransformedBitmap();
            transformed.BeginInit();
            transformed.Source = current;
            transformed.Transform = new ScaleTransform(scale, scale);
            transformed.EndInit();
            transformed.Freeze();
            return transformed;
        }
    }
}
