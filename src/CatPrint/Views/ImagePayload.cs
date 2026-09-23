using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using CatPrint.Imaging;

namespace CatPrint.Views
{
    /// <summary>
    /// Картинка внутри холста: оригинал + настройки + готовое ЧБ-превью.
    /// </summary>
    public sealed class ImagePayload
    {
        public ImageOptions Options { get; set; } = new()
        {
            Dither = DitherMode.FloydSteinberg
        };

        /// <summary>Рамка: FrameStyle (0 = нет). Дорисовывается вокруг.</summary>
        public int Frame { get; set; } = FrameStyle.None;

        /// <summary>Поворот по часовой: 0/90/180/270. До обработки.</summary>
        public int Rotation { get; set; } = 0;

        /// <summary>Зеркало по горизонтали (после поворота).</summary>
        public bool Mirror { get; set; } = false;

        /// <summary>
        /// Обрезка долями ОРИЕНТИРОВАННОЙ картинки (после поворота/зеркала),
        /// как мобильная: 0..1 слева/сверху/справа/снизу.
        /// </summary>
        public double CropL { get; set; } = 0;
        public double CropT { get; set; } = 0;
        public double CropR { get; set; } = 0;
        public double CropB { get; set; } = 0;

        public bool HasCrop =>
            CropL > 0 || CropT > 0 || CropR > 0 || CropB > 0;

        /// <summary>Санитизация долей: clamp 0..0.9, сумма встречных &lt; 1.</summary>
        public void SanitizeCrop()
        {
            CropL = Math.Clamp(CropL, 0, 0.9);
            CropT = Math.Clamp(CropT, 0, 0.9);
            CropR = Math.Clamp(CropR, 0, 0.9);
            CropB = Math.Clamp(CropB, 0, 0.9);
            if (CropL + CropR >= 1) { CropL = 0; CropR = 0; }
            if (CropT + CropB >= 1) { CropT = 0; CropB = 0; }
        }

        public string PngBase64 { get; set; } = string.Empty;

        private BitmapSource? _original;
        private BitmapSource? _preview;

        public BitmapSource? Original
        {
            get
            {
                if (_original == null && !string.IsNullOrEmpty(PngBase64))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(PngBase64);
                        using var ms = new MemoryStream(bytes);
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = ms;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        _original = bmp;
                    }
                    catch { }
                }
                return _original;
            }
        }

        public BitmapSource? PreviewBW => _preview;

        /// <summary>Короткая подпись для холста: что реально применено.</summary>
        public string Summary =>
            $"{Options.Dither.ToDisplayName()} • порог {Options.Threshold}" +
            (Math.Abs(Options.Gamma - 1.0) > 0.001
                ? $" • γ {Options.Gamma:0.00}" : string.Empty) +
            (Frame != FrameStyle.None
                ? $" • рамка: {FrameStyle.DisplayName(Frame).ToLower()}" : string.Empty) +
            (Rotation != 0 ? $" • {Rotation}°" : string.Empty) +
            (Mirror ? " • зерк" : string.Empty) +
            (HasCrop ? " • обрезка" : string.Empty);

        public void SetOriginal(BitmapSource bmp)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            PngBase64 = Convert.ToBase64String(ms.ToArray());
            _original = bmp;
            Refresh();
        }

        /// <summary>
        /// Поворот/зеркало ручным ремапом (те же формулы, что мобильный
        /// ImageTransform): точно попиксельно, без интерполяции WPF.
        /// </summary>
        internal static BitmapSource ApplyRotationMirror(
            BitmapSource src, int rotation, bool mirror)
        {
            var bgra = src.Format == PixelFormats.Bgra32
                ? src
                : (BitmapSource)new FormatConvertedBitmap(
                    src, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            int stride = w * 4;
            byte[] px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);

            int norm = ((rotation % 360) + 360) % 360;
            int nw = (norm == 90 || norm == 270) ? h : w;
            int nh = (norm == 90 || norm == 270) ? w : h;
            var ro = new byte[nw * 4 * nh];
            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    int sx, sy;
                    switch (norm)
                    {
                        case 90: sx = y; sy = h - 1 - x; break;
                        case 180: sx = w - 1 - x; sy = h - 1 - y; break;
                        case 270: sx = w - 1 - y; sy = x; break;
                        default: sx = x; sy = y; break;
                    }
                    Buffer.BlockCopy(px, (sy * w + sx) * 4, ro, (y * nw + x) * 4, 4);
                }
            }
            if (mirror)
            {
                for (int y = 0; y < nh; y++)
                {
                    for (int x = 0; x < nw / 2; x++)
                    {
                        int a = (y * nw + x) * 4;
                        int b = (y * nw + nw - 1 - x) * 4;
                        for (int k = 0; k < 4; k++)
                        {
                            byte t = ro[a + k];
                            ro[a + k] = ro[b + k];
                            ro[b + k] = t;
                        }
                    }
                }
            }
            var bmp = BitmapSource.Create(
                nw, nh, 96, 96, PixelFormats.Bgra32, null, ro, nw * 4);
            bmp.Freeze();
            return bmp;
        }

        public void Refresh()
        {
            var src = Original;
            if (src == null)
            {
                _preview = null;
                return;
            }

            try
            {
                _preview = RenderPreview(
                    src, Options, Frame, Rotation, Mirror,
                    CropL, CropT, CropR, CropB);
            }
            catch
            {
                _preview = null;
            }
        }

        /// <summary>
        /// Вырезать прямоугольник долями (l/t/r/b от краёв). Пустая обрезка — as is.
        /// </summary>
        internal static BitmapSource ApplyCrop(
            BitmapSource src, double l, double t, double r, double b)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            int x = Math.Clamp((int)(l * w), 0, w - 1);
            int y = Math.Clamp((int)(t * h), 0, h - 1);
            int cw = Math.Clamp(w - (int)(l * w) - (int)(r * w), 1, w - x);
            int ch = Math.Clamp(h - (int)(t * h) - (int)(b * h), 1, h - y);
            var crop = new CroppedBitmap(src, new Int32Rect(x, y, cw, ch));
            crop.Freeze();
            return crop;
        }

        /// <summary>
        /// Ориентированный оригинал (поворот/зеркало, БЕЗ пайплайна и обрезки):
        /// поле для выделения области обрезки («режешь то, что видишь»).
        /// </summary>
        internal static BitmapSource OrientedOriginal(
            BitmapSource src, int rotation, bool mirror) =>
            ApplyRotationMirror(src, rotation, mirror);

        /// <summary>
        /// Чистый прогон без кэша: исходник → поворот/зеркало → ОБРЕЗКА →
        /// пайплайн (на внутренней ширине при рамке) → ч/б превью 384px.
        /// Один код для холста и диалога настроек.
        /// </summary>
        public static BitmapSource RenderPreview(
            BitmapSource src, ImageOptions opts, int frame, int rotation, bool mirror,
            double cropL = 0, double cropT = 0, double cropR = 0, double cropB = 0)
        {
            src = ApplyRotationMirror(src, rotation, mirror);
            if (cropL > 0 || cropT > 0 || cropR > 0 || cropB > 0)
                src = ApplyCrop(src, cropL, cropT, cropR, cropB);
            int m = FrameStyle.Margin(frame);
            if (m == 0)
                return new PhotoPipeline().Prepare(src, opts).Preview;
            // Рамка: пайплайн на внутренней ширине + компоновка на 384.
            // Та же схема, что мобильный renderSized ([h,w] везде).
            var pipe = new PhotoPipeline();
            var (black, w, h) = pipe.RenderBool(
                src, opts, RasterConverter.PrinterWidth - m * 2);
            bool[,] framed = FrameStyle.ComposeFramed(black, frame);
            return RasterConverter.ToPreview(
                framed, RasterConverter.PrinterWidth, framed.GetLength(0));
        }
    }
}
