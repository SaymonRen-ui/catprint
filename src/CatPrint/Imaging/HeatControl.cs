using System;

namespace CatPrint.Imaging
{
    /// <summary>
    /// Автожар: снижение плотности на заливке + модуляция по группам строк.
    /// Порт мобильного PrintService (формулы сверены): только считает числа,
    /// в протокол не лезет.
    /// </summary>
    public static class HeatControl
    {
        /// <summary>Строк в группе адаптивного жара.</summary>
        public const int AdaptiveGroup = 8;

        private static readonly int[] PopCount = BuildPopCount();

        private static int[] BuildPopCount()
        {
            var t = new int[256];
            for (int v = 0; v < 256; v++)
            {
                int c = v;
                c -= (c >> 1) & 0x55;
                c = (c & 0x33) + ((c >> 2) & 0x33);
                t[v] = (c + (c >> 4)) & 0x0F;
            }
            return t;
        }

        /// <summary>
        /// Автоплотность: плотная заливка (фото) расплывается от жара,
        /// поэтому жар снижаем; текст (малая заливка) идёт как настроено.
        /// coverage 0..1, user 0x30..0x7A.
        /// </summary>
        public static int AutoIntensity(int user, double coverage)
        {
            if (coverage <= 0.25)
                return Math.Clamp(user, 0x30, 0x7A);
            int cut = Math.Clamp((int)((coverage - 0.25) * 20), 0, 12);
            return Math.Clamp(user - cut, 0x30, user);
        }

        /// <summary>
        /// Жар группы строк по её плотности: редкие и средние тона —
        /// полным/высоким жаром, плотная заливка — мягко.
        /// </summary>
        public static int GroupHeat(int user, double coverage)
        {
            int u = Math.Clamp(user, 0x30, 0x7A);
            const int floor = 0x40;
            if (u <= floor || coverage <= 0.30)
                return u;
            double f = Math.Clamp((coverage - 0.30) / (0.65 - 0.30), 0.0, 1.0);
            return Math.Clamp((int)(u - f * (u - floor)), floor, u);
        }

        /// <summary>Доля чёрных точек в строке растра.</summary>
        public static double LineCoverage(byte[] line)
        {
            long n = 0;
            foreach (byte b in line)
                n += PopCount[b];
            return n / (double)(line.Length * 8);
        }

        /// <summary>Доля чёрных точек во всём растре.</summary>
        public static double Coverage(byte[] raster)
        {
            if (raster == null || raster.Length == 0)
                return 0;
            long n = 0;
            foreach (byte b in raster)
                n += PopCount[b];
            return n / (double)(raster.Length * 8);
        }
    }
}
