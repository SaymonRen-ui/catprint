using System;

namespace CatPrint.Imaging
{
    /// <summary>
    /// Рамка вокруг блока (картинка/текст/QR). Порт мобильного FrameStyle:
    /// те же 7 стилей, те же поля. Геометрия узора — DocRender.FramePixel.
    /// </summary>
    public static class FrameStyle
    {
        public const int None = 0;
        public const int Thin = 1;
        public const int Thick = 2;
        public const int Double = 3;
        public const int Wavy = 4;
        public const int Dashed = 5;
        public const int Dotted = 6;
        public const int Zigzag = 7;

        public static int[] All() =>
            new[] { None, Thin, Thick, Double, Wavy, Dashed, Dotted, Zigzag };

        public static string DisplayName(int i) => Sanitize(i) switch
        {
            Thin => "Тонкая",
            Thick => "Толстая",
            Double => "Двойная",
            Wavy => "Волнистая",
            Dashed => "Пунктир",
            Dotted => "Точки",
            Zigzag => "Зигзаг",
            _ => "Без рамки"
        };

        public static int Sanitize(int i) =>
            i < None || i > Zigzag ? None : i;

        /// <summary>
        /// Внешнее поле с каждой стороны, px: рамка + 6px отступа до контента.
        /// </summary>
        public static int Margin(int i) => Sanitize(i) switch
        {
            Thin => 8,
            Thick => 11,
            Double => 13,
            Wavy => 13,
            Dashed => 9,
            Dotted => 9,
            Zigzag => 12,
            _ => 0
        };

        /// <summary>
        /// Чёрный ли пиксель рамки в (x, y). e — глубина от ближайшего края,
        /// t — координата вдоль него.
        /// </summary>
        public static bool FramePixel(int x, int y, int w, int h, int f)
        {
            int e = Math.Min(Math.Min(x, y), Math.Min(w - 1 - x, h - 1 - y));
            int t = Math.Min(y, h - 1 - y) <= Math.Min(x, w - 1 - x) ? x : y;
            return Sanitize(f) switch
            {
                Thin => e >= 6 && e <= 7,
                Thick => e >= 6 && e <= 10,
                Double => (e >= 6 && e <= 7) || (e >= 11 && e <= 12),
                // Волна: осевая 8 ± 3, период 24, толщина ~3.
                Wavy => Math.Abs(e - (8 + 3 * Math.Sin(t * Math.PI / 12))) < 1.5,
                // Пунктир 10/6, толщина 3.
                Dashed => e >= 6 && e <= 8 && Mod(t, 16) < 10,
                // Точки 3px с шагом 8.
                Dotted => e >= 6 && e <= 8 && Mod(t, 8) < 3,
                // Зигзаг: треугольная волна 0..3 вокруг глубины 7, период 16.
                Zigzag => Math.Abs(e - (7 + Math.Abs(Mod(t, 16) - 8) / 8.0 * 3.0)) < 1.2,
                _ => false
            };
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;

        /// <summary>
        /// Маска рамки w×h (true = чёрное). Контентная зона внутри полей
        /// не тронута.
        /// </summary>
        public static bool[,] Mask(int w, int h, int frame)
        {
            var res = new bool[h, w];
            int f = Sanitize(frame);
            if (f == None)
                return res;
            int m = Margin(f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (x >= m && x < w - m && y >= m && y < h - m)
                        continue;
                    if (FramePixel(x, y, w, h, f))
                        res[y, x] = true;
                }
            }
            return res;
        }

        /// <summary>
        /// Внутренний растр на белом поле 384px + рамка вокруг.
        /// Та же компоновка, что мобильный renderSized.
        /// </summary>
        public static bool[,] ComposeFramed(bool[,] inner, int frame)
        {
            const int fullW = RasterConverter.PrinterWidth;
            int m = Margin(Sanitize(frame));
            int iw = inner.GetLength(1);
            int ih = inner.GetLength(0);
            int fh = ih + m * 2;
            var res = new bool[fh, fullW];
            for (int y = 0; y < ih; y++)
                for (int x = 0; x < iw; x++)
                    res[y + m, x + m] = inner[y, x];
            var mask = Mask(fullW, fh, frame);
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fullW; x++)
                    res[y, x] |= mask[y, x];
            return res;
        }
    }
}
