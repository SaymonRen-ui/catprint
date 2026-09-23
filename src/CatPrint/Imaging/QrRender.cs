using System;
using System.Collections.Generic;
using ZXing;
using ZXing.QrCode.Internal;

namespace CatPrint.Imaging
{
    /// <summary>
    /// QR в 1-битную матрицу для термопечати. Порт мобильного QrRender:
    /// матрица + quiet zone 4 модуля, масштаб целым коэффициентом.
    /// Чёрное на белом, без инверсии. null — пустой текст или не влез.
    /// </summary>
    public static class QrRender
    {
        public static int TargetPx(int size) => size switch
        {
            0 => 128,
            2 => 256,
            _ => 192
        };

        public static string SizeName(int size) => size switch
        {
            0 => "Малый",
            2 => "Крупный",
            _ => "Средний"
        };

        public static bool[,]? Render(string text, int targetPx)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                var hints = new Dictionary<EncodeHintType, object>
                {
                    [EncodeHintType.CHARACTER_SET] = "UTF-8",
                    [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
                    [EncodeHintType.MARGIN] = 0
                };
                var writer = new ZXing.QrCode.QRCodeWriter();
                var m = writer.encode(
                    text, BarcodeFormat.QR_CODE, 0, 0, hints);
                int mw = m.Width;
                if (mw <= 0)
                    return null;

                const int q = 4;
                int total = mw + q * 2;
                int k = Math.Max(1, targetPx / total);
                int w = total * k;
                var black = new bool[w, w];
                for (int y = 0; y < total; y++)
                {
                    for (int x = 0; x < total; x++)
                    {
                        bool v = x >= q && x < q + mw &&
                                 y >= q && y < q + mw &&
                                 m[x - q, y - q];
                        if (!v)
                            continue;
                        for (int dy = 0; dy < k; dy++)
                            for (int dx = 0; dx < k; dx++)
                                black[y * k + dy, x * k + dx] = true;
                    }
                }
                return black;
            }
            catch
            {
                return null;
            }
        }
    }
}
