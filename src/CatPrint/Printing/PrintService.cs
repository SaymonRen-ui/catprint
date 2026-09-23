using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CatPrint.Printing
{
    /// <summary>
    /// Печать готового 1-битного растра 384 px.
    /// Строго как рабочий PrinCat: A2 → 50мс → A9 → строки по 48 байт
    /// (15мс) → дренаж 50мс → AD → 3000мс. Высота A9 обязана точно
    /// равняться числу строк растра, никакого паддинга.
    /// </summary>
    public sealed class PrintService
    {
        private readonly PrinterConnection _connection;

        public PrintService(PrinterConnection connection)
        {
            _connection = connection
                ?? throw new ArgumentNullException(nameof(connection));
        }

        public async Task PrintAsync(
            byte[] raster,
            int width,
            int height,
            byte intensity = 0x5D,
            IProgress<double>? progress = null,
            CancellationToken ct = default,
            int lineDelayMs = 15,
            int blockLines = 40,
            int blockPauseMs = 0,
            Action<string>? log = null,
            bool splitEnabled = false,
            int splitLines = 96,
            int splitPauseMs = 1500,
            bool autoDensity = false,
            bool adaptiveHeat = false)
        {
            if (raster == null)
                throw new ArgumentNullException(nameof(raster));

            if (width != Mxw01Protocol.PrinterWidth)
                throw new InvalidOperationException(
                    $"Неверная ширина: {width}. Нужно {Mxw01Protocol.PrinterWidth}.");

            if (height <= 0)
                throw new InvalidOperationException("Высота должна быть больше нуля.");

            int expected = Mxw01Protocol.BytesPerLine * height;
            if (raster.Length != expected)
                throw new InvalidOperationException(
                    $"Неверный размер растра. Нужно {expected}, получено {raster.Length}.");

            if (!_connection.IsConnected)
                throw new InvalidOperationException("Принтер не подключён.");

            ct.ThrowIfCancellationRequested();

            // Нарезка на части — опция для экспериментов (по умолчанию выкл).
            // Паддинга нет: каждая часть идёт ровно своей высотой.
            var segments = SplitRaster(raster, height, splitEnabled, splitLines);

            log?.Invoke(
                $"TX: A2=0x{intensity:X2}, всего строк={height}, " +
                $"заданий={segments.Count}, " +
                $"паузы: строка={lineDelayMs}мс пачка={blockLines}/{blockPauseMs}мс");

            // A2 — плотность (один раз на всю печать).
            // Адаптив держит базовый жар сам (модуляция по группам),
            // глобальное снижение тут не нужно — как на мобильном.
            if (!adaptiveHeat && autoDensity)
            {
                double cov = Imaging.HeatControl.Coverage(raster);
                int auto = Imaging.HeatControl.AutoIntensity(intensity, cov);
                if (auto != intensity)
                {
                    log?.Invoke(
                        $"Автоплотность: 0x{intensity:X2} → 0x{auto:X2} " +
                        $"(заливка {(int)(cov * 100)}%)");
                }
                intensity = (byte)auto;
            }
            await _connection.SendCommandAsync(
                Mxw01Protocol.Intensity(intensity), ct);
            await Task.Delay(50, ct);
            int lastHeat = intensity;
            int baseHeat = intensity;

            int grandTotal = 0;
            foreach (var seg in segments)
                grandTotal += seg.Data.Length / Mxw01Protocol.BytesPerLine;

            int doneLines = 0;
            int doneReal = 0;

            for (int s = 0; s < segments.Count; s++)
            {
                ct.ThrowIfCancellationRequested();

                var (segRaster, segHeight) = segments[s];

                // Строгое правило рабочего проекта: строк в потоке
                // ровно столько, сколько заявлено в A9. Без паддинга.
                if (segRaster.Length != segHeight * Mxw01Protocol.BytesPerLine)
                {
                    throw new InvalidOperationException(
                        $"Растр части {s + 1} не соответствует высоте: " +
                        $"{segRaster.Length} байт при h={segHeight}.");
                }

                int totalLines = segHeight;

                if (segments.Count > 1)
                    log?.Invoke($"TX: задание {s + 1}/{segments.Count}: h={segHeight}");

                // A9 — запрос печати (реальная высота части)
                await _connection.SendCommandAsync(
                    Mxw01Protocol.PrintRequest(segHeight), ct);

                // AE03 — построчно по 48 байт, одна строка = одна запись
                byte[] line = new byte[Mxw01Protocol.BytesPerLine];
                for (int l = 0; l < totalLines; l++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Адаптив: в начале группы смотрим плотность следующих
                    // строк и при нужде переключаем A2 (без разрыва потока).
                    if (adaptiveHeat && l % Imaging.HeatControl.AdaptiveGroup == 0)
                    {
                        int gEnd = Math.Min(
                            l + Imaging.HeatControl.AdaptiveGroup, totalLines);
                        long n = 0;
                        for (int i = l * Mxw01Protocol.BytesPerLine;
                             i < gEnd * Mxw01Protocol.BytesPerLine; i++)
                        {
                            int v = segRaster[i];
                            v -= (v >> 1) & 0x55;
                            v = (v & 0x33) + ((v >> 2) & 0x33);
                            n += (v + (v >> 4)) & 0x0F;
                        }
                        double cov = n / (double)(
                            (gEnd - l) * Mxw01Protocol.BytesPerLine * 8);
                        int heat = Imaging.HeatControl.GroupHeat(baseHeat, cov);
                        if (heat != lastHeat)
                        {
                            log?.Invoke(
                                $"TX: жар 0x{lastHeat:X2} → 0x{heat:X2}");
                            await _connection.SendCommandAsync(
                                Mxw01Protocol.Intensity((byte)heat), ct);
                            await Task.Delay(20, ct);
                            lastHeat = heat;
                        }
                    }

                    Buffer.BlockCopy(
                        segRaster, l * Mxw01Protocol.BytesPerLine,
                        line, 0, Mxw01Protocol.BytesPerLine);

                    try
                    {
                        await _connection.SendRasterAsync(line, ct);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Печать оборвалась: задание {s + 1}/{segments.Count}, " +
                            $"строка {l + 1} из {totalLines}: {ex.Message}", ex);
                    }

                    doneLines++;
                    progress?.Report(0.05 + 0.90 * doneLines / grandTotal);

                    if (l + 1 < totalLines)
                    {
                        if (blockLines > 0 && blockPauseMs > 0 &&
                            (l + 1) % blockLines == 0)
                        {
                            await Task.Delay(blockPauseMs, ct);
                        }
                        else if (lineDelayMs > 0)
                        {
                            await Task.Delay(lineDelayMs, ct);
                        }
                    }
                }

                // Даём последней строке уйти в принтер перед AD
                await Task.Delay(50, ct);

                // AD — flush
                await _connection.SendCommandAsync(Mxw01Protocol.Flush(), ct);

                doneReal += segHeight;

                if (s + 1 < segments.Count)
                {
                    // Пауза между частями + время на обработку AD
                    int pause = Math.Max(splitPauseMs, 3000);
                    log?.Invoke($"TX: пауза между заданиями {pause}мс");
                    await Task.Delay(pause, ct);
                }
                else
                {
                    // Финал: принтер допечатывает задание
                    await Task.Delay(3000, ct);
                }
            }

            log?.Invoke($"TX: готово, строк контента {doneReal}/{height}");

            progress?.Report(1.0);
        }

        /// <summary>
        /// Режет растр на части не выше splitLines строк.
        /// </summary>
        private static List<(byte[] Data, int Height)> SplitRaster(
            byte[] raster, int height, bool splitEnabled, int splitLines)
        {
            var result = new List<(byte[], int)>();

            if (!splitEnabled || splitLines <= 0 || height <= splitLines)
            {
                result.Add((raster, height));
                return result;
            }

            int stride = Mxw01Protocol.BytesPerLine;
            int done = 0;
            while (done < height)
            {
                int h = Math.Min(splitLines, height - done);
                byte[] part = new byte[h * stride];
                Buffer.BlockCopy(raster, done * stride, part, 0, part.Length);
                result.Add((part, h));
                done += h;
            }

            return result;
        }
    }
}
