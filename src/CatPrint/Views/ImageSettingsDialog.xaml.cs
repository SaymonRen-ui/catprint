using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using CatPrint.Imaging;

namespace CatPrint.Views
{
    /// <summary>
    /// Настройки картинки: дизеринг, яркость, контраст, порог.
    /// </summary>
    public partial class ImageSettingsDialog : Window
    {
        private readonly BitmapSource _source;

        public ImageOptions ResultOptions { get; private set; } = new();
        public BitmapSource? ResultPreview { get; private set; }
        public int ResultFrame { get; private set; } = 0;
        public int ResultRotation { get; private set; } = 0;
        public bool ResultMirror { get; private set; } = false;
        public double ResultCropL { get; private set; } = 0;
        public double ResultCropT { get; private set; } = 0;
        public double ResultCropR { get; private set; } = 0;
        public double ResultCropB { get; private set; } = 0;

        /// <summary>
        /// Живое применение (для применения на холст без закрытия диалога).
        /// Рамка/поворот/зеркало применяются сразу на живой payload, если дан.
        /// </summary>
        public event Action<ImageOptions>? OptionsChanged;

        private int _frame;
        private int _rotation;
        private readonly ImagePayload? _live;

        // Обрезка долями ориентированной картинки (как мобильная).
        private double _cropL;
        private double _cropT;
        private double _cropR;
        private double _cropB;
        private bool _cropMode;
        private Rect _cropSel;
        private bool _cropHasSel;
        private Point _cropAnchor;

        public ImageSettingsDialog(
            BitmapSource source, ImageOptions? current = null, ImagePayload? live = null)
        {
            _source = source;
            _live = live;
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);

            if (current != null)
            {
                CmbDither.SelectedIndex = Math.Clamp((int)current.Dither, 0, 9);
                SldBright.Value = current.Brightness;
                SldContrast.Value = current.Contrast;
                SldThreshold.Value = current.Threshold;
                SldGamma.Value = Math.Clamp(current.Gamma, 0.2, 3.0);
                SldSaturation.Value = Math.Clamp(current.Saturation, 0, 200);
                ChkInvert.IsChecked = current.Invert;
            }
            else
            {
                CmbDither.SelectedIndex = 1;
            }

            _frame = FrameStyle.Sanitize(live?.Frame ?? 0);
            _rotation = live?.Rotation ?? 0;
            if (_rotation != 0 && _rotation != 90 && _rotation != 180 && _rotation != 270)
                _rotation = 0;
            _cropL = live?.CropL ?? 0;
            _cropT = live?.CropT ?? 0;
            _cropR = live?.CropR ?? 0;
            _cropB = live?.CropB ?? 0;
            CmbFrame.SelectedIndex = _frame;
            BtnRotate.Content = $"⟳ {_rotation}°";
            ChkMirror.IsChecked = live?.Mirror ?? false;

            Update();
        }

        private void Opt_Changed(object sender, RoutedEventArgs e) => Update();

        private void CmbFrame_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;
            _frame = FrameStyle.Sanitize(CmbFrame.SelectedIndex);
            Update();
        }

        private void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            _rotation = (_rotation + 90) % 360;
            BtnRotate.Content = $"⟳ {_rotation}°";
            Update();
        }

        private void Opt_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;
            Update();
        }

        private void Update()
        {
            if (CmbDither == null || PreviewImage == null)
                return;

            RunBright.Text = ((int)SldBright.Value).ToString();
            RunContrast.Text = ((int)SldContrast.Value).ToString();
            RunThreshold.Text = ((int)SldThreshold.Value).ToString();
            RunGamma.Text = SldGamma.Value.ToString("0.00");
            RunSaturation.Text = ((int)SldSaturation.Value).ToString();

            // Порог действует только в режимах «Порог» и «Случайный» —
            // в остальных он зашит (128), слайдер гасим как на мобильном.
            var dm = CmbDither.SelectedIndex >= 0
                ? (DitherMode)CmbDither.SelectedIndex
                : DitherMode.FloydSteinberg;
            SldThreshold.IsEnabled =
                dm == DitherMode.Threshold || dm == DitherMode.Random;

            var opts = new ImageOptions
            {
                Dither = dm,
                Brightness = SldBright.Value,
                Contrast = SldContrast.Value,
                Threshold = (int)SldThreshold.Value,
                Gamma = SldGamma.Value,
                Saturation = SldSaturation.Value,
                Invert = ChkInvert.IsChecked == true
            };
            bool mirror = ChkMirror.IsChecked == true;

            try
            {
                var preview = ImagePayload.RenderPreview(
                    _source, opts, _frame, _rotation, mirror,
                    _cropL, _cropT, _cropR, _cropB);
                PreviewImage.Source = preview;
                TxtInfo.Text = $"{preview.PixelWidth}×{preview.PixelHeight}px";
                ResultOptions = opts;
                ResultPreview = preview;
                ResultFrame = _frame;
                ResultRotation = _rotation;
                ResultMirror = mirror;
                ResultCropL = _cropL;
                ResultCropT = _cropT;
                ResultCropR = _cropR;
                ResultCropB = _cropB;
                if (_live != null)
                {
                    _live.Frame = _frame;
                    _live.Rotation = _rotation;
                    _live.Mirror = mirror;
                    _live.CropL = _cropL;
                    _live.CropT = _cropT;
                    _live.CropR = _cropR;
                    _live.CropB = _cropB;
                }
                ShowCurrent();
                OptionsChanged?.Invoke(opts);
            }
            catch (Exception ex)
            {
                TxtInfo.Text = ex.Message;
            }
        }

        private void BtnAuto_Click(object sender, RoutedEventArgs e) =>
            AutoLevels();

        /// <summary>
        /// Автоуровни: тянем гистограмму (1–99 перцентили) на 8..247
        /// подбором контраста и яркости.
        /// </summary>
        private void AutoLevels()
        {
            try
            {
                // Уменьшаем для скорости анализа
                BitmapSource small = _source;
                if (_source.PixelWidth > 384)
                {
                    double k = 384.0 / _source.PixelWidth;
                    var tb = new TransformedBitmap(
                        _source, new ScaleTransform(k, k));
                    tb.Freeze();
                    small = tb;
                }

                var bgra = small.Format == PixelFormats.Bgra32
                    ? small
                    : (BitmapSource)new FormatConvertedBitmap(
                        small, PixelFormats.Bgra32, null, 0);

                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                int stride = w * 4;
                byte[] px = new byte[stride * h];
                bgra.CopyPixels(px, stride, 0);

                int[] hist = new int[256];
                for (int i = 0; i < px.Length; i += 4)
                {
                    int lum = (int)(0.299 * px[i + 2] + 0.587 * px[i + 1] + 0.114 * px[i]);
                    hist[Math.Clamp(lum, 0, 255)]++;
                }

                int total = w * h;
                int p1 = 0, p99 = 255, acc = 0;
                for (int v = 0; v < 256; v++)
                {
                    acc += hist[v];
                    if (acc >= total * 0.01) { p1 = v; break; }
                }
                acc = 0;
                for (int v = 255; v >= 0; v--)
                {
                    acc += hist[v];
                    if (acc >= total * 0.01) { p99 = v; break; }
                }

                if (p99 - p1 < 8)
                    return;

                double cf = 239.0 / (p99 - p1);
                double a = cf * 255.0;
                double c = 259.0 * (a - 255.0) / (259.0 + a);
                double off = 8.0 - ((p1 - 128.0) * cf + 128.0);

                SldContrast.Value = Math.Clamp(c / 2.55, -100, 100);
                SldBright.Value = Math.Clamp(off / 2.55, -100, 100);
            }
            catch { }
        }

        /// <summary>
        /// Пресет для фото: диффузия ошибок (без сетки и кружков)
        /// + автоуровни.
        /// </summary>
        private void BtnPhotoPreset_Click(object sender, RoutedEventArgs e)
        {
            CmbDither.SelectedIndex = 1; // Флойд–Стейнберг
            SldGamma.Value = 1.0;
            ChkInvert.IsChecked = false;
            AutoLevels();
        }

        /// <summary>
        /// Пресет для графики/текста: жёсткий порог, чёткие края.
        /// </summary>
        private void BtnGraphicPreset_Click(object sender, RoutedEventArgs e)
        {
            CmbDither.SelectedIndex = 0; // Порог
            SldBright.Value = 0;
            SldContrast.Value = 0;
            SldThreshold.Value = 128;
            SldGamma.Value = 1.0;
            ChkInvert.IsChecked = false;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        // ============================================================
        // ОБРЕЗКА: выделение области мышью на ориентированном оригинале
        // ============================================================

        private void BtnCrop_Click(object sender, RoutedEventArgs e) =>
            SetCropMode(!_cropMode);

        private void SetCropMode(bool on)
        {
            _cropMode = on;
            _cropHasSel = false;
            CropCanvas.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            CropRect.Visibility = Visibility.Collapsed;
            CropRectDark.Visibility = Visibility.Collapsed;
            TxtCropHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            CropActions.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BtnCrop.Content = on ? "✂ Выделение…" : "✂ Обрезать";
            ShowCurrent();
        }

        private Point CropFraction(Point p)
        {
            double w = PreviewImage.ActualWidth;
            double h = PreviewImage.ActualHeight;
            if (w <= 0 || h <= 0)
                return new Point(0, 0);
            return new Point(
                Math.Clamp(p.X / w, 0, 1),
                Math.Clamp(p.Y / h, 0, 1));
        }

        private void DrawCropSel()
        {
            if (!_cropHasSel)
            {
                CropRect.Visibility = Visibility.Collapsed;
                CropRectDark.Visibility = Visibility.Collapsed;
                return;
            }
            // Stretch=None: пиксели 1:1 к Actual-размеру.
            double w = PreviewImage.ActualWidth;
            double h = PreviewImage.ActualHeight;
            double l = _cropSel.X * w;
            double t = _cropSel.Y * h;
            double sw = _cropSel.Width * w;
            double sh = _cropSel.Height * h;
            foreach (var r in new[] { CropRectDark, CropRect })
            {
                Canvas.SetLeft(r, l);
                Canvas.SetTop(r, t);
                r.Width = Math.Max(0, sw);
                r.Height = Math.Max(0, sh);
                r.Visibility = Visibility.Visible;
            }
        }

        private void Crop_Down(object sender, MouseButtonEventArgs e)
        {
            _cropAnchor = CropFraction(e.GetPosition(PreviewImage));
            _cropSel = new Rect(_cropAnchor, _cropAnchor);
            _cropHasSel = true;
            DrawCropSel();
            CropCanvas.CaptureMouse();
        }

        private void Crop_Move(object sender, MouseEventArgs e)
        {
            if (!_cropHasSel || e.LeftButton != MouseButtonState.Pressed)
                return;
            var f = CropFraction(e.GetPosition(PreviewImage));
            _cropSel = new Rect(
                Math.Min(_cropAnchor.X, f.X),
                Math.Min(_cropAnchor.Y, f.Y),
                Math.Abs(f.X - _cropAnchor.X),
                Math.Abs(f.Y - _cropAnchor.Y));
            DrawCropSel();
        }

        private void Crop_Up(object sender, MouseButtonEventArgs e)
        {
            CropCanvas.ReleaseMouseCapture();
            DrawCropSel();
        }

        private void BtnCropApply_Click(object sender, RoutedEventArgs e)
        {
            // Слишком мелкое выделение — игнорируем (не выходим из режима).
            if (_cropHasSel && _cropSel.Width >= 0.02 && _cropSel.Height >= 0.02)
            {
                _cropL = _cropSel.X;
                _cropT = _cropSel.Y;
                _cropR = 1.0 - _cropSel.Right;
                _cropB = 1.0 - _cropSel.Bottom;
                SetCropMode(false);
                Update();
            }
        }

        private void BtnCropReset_Click(object sender, RoutedEventArgs e)
        {
            _cropL = _cropT = _cropR = _cropB = 0;
            SetCropMode(false);
            Update();
        }

        /// <summary>
        /// Переключатель вида: 1-битный растр или оригинал — прямо здесь.
        /// </summary>
        private void ChkBit_Changed(object sender, RoutedEventArgs e) =>
            ShowCurrent();

        private void ShowCurrent()
        {
            if (PreviewImage == null)
                return;

            // Режим обрезки: всегда ориентированный оригинал 1:1 —
            // «режешь то, что видишь» (поворот/зеркало уже применены).
            if (_cropMode)
            {
                try
                {
                    bool mirror = ChkMirror.IsChecked == true;
                    var oriented = ImagePayload.OrientedOriginal(
                        _source, _rotation, mirror);
                    PreviewImage.Source = oriented;
                    PreviewImage.Stretch = Stretch.None;
                    TxtInfo.Text =
                        $"{oriented.PixelWidth}×{oriented.PixelHeight}px • выдели область";
                }
                catch (Exception ex)
                {
                    TxtInfo.Text = ex.Message;
                }
                return;
            }

            bool bit = ChkBit?.IsChecked != false;

            // Подпись кнопки = текущее состояние, чтобы не путать с кнопкой-действием
            if (ChkBit != null)
                ChkBit.Content = bit ? "👁 1-бит ✓" : "🖼 Оригинал";

            if (bit)
            {
                PreviewImage.Source = ResultPreview;
                PreviewImage.Stretch = Stretch.None;
            }
            else
            {
                PreviewImage.Source = _source;
                PreviewImage.Stretch = Stretch.Uniform;
            }
        }
    }
}
