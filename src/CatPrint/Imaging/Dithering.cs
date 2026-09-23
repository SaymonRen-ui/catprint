using System;

namespace CatPrint.Imaging
{
    /// <summary>
    /// Дизеринги: порог, Флойд–Стейнберг, Аткинсон, упорядоченный Байер.
    /// Вход — gray 0..255, выход — black[y,x] (true = чёрный = печатать).
    /// </summary>
    public static class Dithering
    {
        public static bool[,] Apply(float[,] gray, int width, int height, DitherMode mode, int threshold)
        {
            return mode switch
            {
                DitherMode.Threshold => ApplyThreshold(gray, width, height, threshold),
                DitherMode.FloydSteinberg => ApplyKernel(gray, width, height,
                    new (int, int, float)[]
                    {
                        (1, 0, 7), (-1, 1, 3), (0, 1, 5), (1, 1, 1)
                    }, 16),
                DitherMode.Atkinson => ApplyAtkinson(gray, width, height),
                DitherMode.OrderedBayer4x4 => ApplyBayer(gray, width, height, Bayer4x4, 4),
                DitherMode.OrderedBayer8x8 => ApplyBayer(gray, width, height, Bayer8x8, 8),
                DitherMode.Jarvis => ApplyKernel(gray, width, height,
                    new (int, int, float)[]
                    {
                        (1, 0, 7), (2, 0, 5),
                        (-2, 1, 3), (-1, 1, 5), (0, 1, 7), (1, 1, 5), (2, 1, 3),
                        (-2, 2, 1), (-1, 2, 3), (0, 2, 5), (1, 2, 3), (2, 2, 1)
                    }, 48),
                DitherMode.Stucki => ApplyKernel(gray, width, height,
                    new (int, int, float)[]
                    {
                        (1, 0, 8), (2, 0, 4),
                        (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2),
                        (-2, 2, 1), (-1, 2, 2), (0, 2, 4), (1, 2, 2), (2, 2, 1)
                    }, 42),
                DitherMode.Burkes => ApplyKernel(gray, width, height,
                    new (int, int, float)[]
                    {
                        (1, 0, 8), (2, 0, 4),
                        (-2, 1, 2), (-1, 1, 4), (0, 1, 8), (1, 1, 4), (2, 1, 2)
                    }, 32),
                DitherMode.Sierra => ApplyKernel(gray, width, height,
                    new (int, int, float)[]
                    {
                        (1, 0, 5), (2, 0, 3),
                        (-2, 1, 2), (-1, 1, 4), (0, 1, 5), (1, 1, 4), (2, 1, 2),
                        (-1, 2, 2), (0, 2, 3), (1, 2, 2)
                    }, 32),
                DitherMode.Random => ApplyRandom(gray, width, height, threshold),
                _ => ApplyThreshold(gray, width, height, threshold)
            };
        }

        private static bool[,] ApplyThreshold(float[,] gray, int w, int h, int threshold)
        {
            var black = new bool[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    black[y, x] = gray[y, x] < threshold;
            return black;
        }

        private static bool[,] ApplyKernel(
            float[,] src, int w, int h,
            (int Dx, int Dy, float Weight)[] kernel, float divisor)
        {
            float[,] g = (float[,])src.Clone();
            var black = new bool[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float old = g[y, x];
                    bool b = old < 128;
                    black[y, x] = b;
                    float err = (old - (b ? 0 : 255)) / divisor;

                    foreach (var (dx, dy, weight) in kernel)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                            g[ny, nx] += err * weight;
                    }
                }
            }
            return black;
        }

        private static bool[,] ApplyRandom(float[,] gray, int w, int h, int threshold)
        {
            // Фиксированный seed: превью в диалоге и на бумаге совпадают.
            var rnd = new Random(1234);
            var black = new bool[h, w];

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double v = gray[y, x] + (rnd.NextDouble() - 0.5) * 96.0;
                    black[y, x] = v < threshold;
                }

            return black;
        }

        private static bool[,] ApplyAtkinson(float[,] src, int w, int h)
        {
            float[,] g = (float[,])src.Clone();
            var black = new bool[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float old = g[y, x];
                    bool b = old < 128;
                    black[y, x] = b;
                    float err = (old - (b ? 0 : 255)) / 8f;

                    Spread(g, w, h, x + 1, y, err);
                    Spread(g, w, h, x + 2, y, err);
                    Spread(g, w, h, x - 1, y + 1, err);
                    Spread(g, w, h, x, y + 1, err);
                    Spread(g, w, h, x + 1, y + 1, err);
                    Spread(g, w, h, x, y + 2, err);
                }
            }
            return black;
        }

        private static void Spread(float[,] g, int w, int h, int x, int y, float err)
        {
            if (x >= 0 && x < w && y >= 0 && y < h)
                g[y, x] += err;
        }

        private static readonly int[,] Bayer4x4 = new int[,]
        {
            { 0, 8, 2, 10 },
            { 12, 4, 14, 6 },
            { 3, 11, 1, 9 },
            { 15, 7, 13, 5 }
        };

        private static readonly int[,] Bayer8x8 = new int[,]
        {
            { 0,32, 8,40, 2,34,10,42 },
            { 48,16,56,24,50,18,58,26 },
            { 12,44, 4,36,14,46, 6,38 },
            { 60,28,52,20,62,30,54,22 },
            { 3,35,11,43, 1,33, 9,41 },
            { 51,19,59,27,49,17,57,25 },
            { 15,47, 7,39,13,45, 5,37 },
            { 63,31,55,23,61,29,53,21 }
        };

        private static bool[,] ApplyBayer(float[,] gray, int w, int h, int[,] matrix, int n)
        {
            var black = new bool[h, w];
            float scale = 255f / (n * n);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = (matrix[y % n, x % n] + 0.5f) * scale;
                    black[y, x] = gray[y, x] < t;
                }

            return black;
        }
    }
}
