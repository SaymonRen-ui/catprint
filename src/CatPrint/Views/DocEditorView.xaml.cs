using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using Microsoft.Win32;

using CatPrint.Imaging;

namespace CatPrint.Views
{
    public enum DividerStyle
    {
        Solid,
        Dashed,
        Dotted,
        Double
    }

    /// <summary>
    /// Холст-бумага 384px: единый документ как в Word.
    /// Enter — новый абзац со своим выравниванием, Win+. — эмодзи Windows.
    /// Печать и «Как напечатается» рендерят один и тот же живой холст.
    /// </summary>
    public partial class DocEditorView : UserControl
    {
        public event Action? DocumentChanged;

        private const double PaperWidth = 384;
        private const double ContentWidth = 368; // ширина набора текста (поля 8+8)
        private const double ImageWidth = 384; // картинки — в родных точках 1:1
        private const int MaxPrintHeight = 4200;

        private readonly DispatcherTimer _debTimer;
        private bool _syncing;
        private bool _ready;

        /// <summary>Настройки задаёт главное окно (плотность и пр.).</summary>
        private AppSettings? _settings;
        public AppSettings? Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                if (_ready)
                    RefreshFontList(_lastFont);
            }
        }

        public bool HasBlocks => _ready && !IsDocumentEmpty();

        public DocEditorView()
        {
            InitializeComponent();

            _debTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _debTimer.Tick += (s, e) =>
            {
                _debTimer.Stop();
                if (ChkBit.IsChecked == true)
                    RefreshBitPreview();
            };

            RefreshFontList("Segoe UI");

            CmbSize.ItemsSource = new[] { "14", "18", "22", "28", "36", "48", "64" };
            CmbSize.SelectedItem = "24";
            CmbSpacing.SelectedIndex = 1;

            Picker.Picked += InsertEmoji;

            Loaded += (s, e) => DrawRuler();

            InitUndo();
            BuildDefaultDoc();
            _ready = true;
            UpdateInfo();
            PushChange(); // базовая точка для Undo
        }

        // ============================================================
        // ПЛОТНОСТЬ ПЕЧАТИ (слайдер в ленте, хранится в настройках)
        // ============================================================

        /// <summary>
        /// Байт 0x30..0x7A ↔ слайдер 0..100. 0x5D ≈ 61.
        /// Диапазон уже проверенного: тестовый проект слал только 0x5D,
        /// значения вне 0x30..0x7A могут вешать прошивку.
        /// </summary>
        public static byte PowerSliderToByte(double slider) =>
            (byte)Math.Clamp(48 + (int)Math.Round(slider * 0.74), 0x30, 0x7A);

        public static double PowerByteToSlider(byte b) =>
            Math.Clamp((b - 48) / 0.74, 0, 100);

        /// <summary>Понятное отображение: «68% · Стандарт».</summary>
        public static string PowerDisplay(double slider)
        {
            int pct = (int)Math.Round(Math.Clamp(slider, 0, 100));
            string word = pct switch
            {
                <= 30 => "Эконом",
                <= 70 => "Стандарт",
                _ => "Максимум"
            };
            return $"{pct}% · {word}";
        }

        // ============================================================
        // ОТМЕНА / ВОЗВРАТ — свой стек снапшотов.
        // Встроенный Undo в RichTextBox криво переживает встроенные
        // картинки (теряет Tag с настройками), поэтому снапшотим
        // документ в наш формат и восстанавливаем из него.
        // ============================================================

        private readonly Stack<JsonObject> _undoStack = new();
        private readonly Stack<JsonObject> _redoStack = new();
        private const int MaxUndo = 50;
        private bool _restoring;
        private readonly DispatcherTimer _snapTimer = new()
        {
            Interval = TimeSpan.FromSeconds(1.2)
        };

        private void InitUndo()
        {
            _snapTimer.Tick += (s, e) =>
            {
                _snapTimer.Stop();
                PushChange();
            };
        }

        /// <summary>Зафиксировать текущее состояние как точку возврата.</summary>
        private void PushChange()
        {
            if (!_ready || _restoring)
                return;

            try
            {
                _undoStack.Push(SaveV2());
            }
            catch
            {
                return;
            }

            if (_undoStack.Count > MaxUndo)
            {
                var keep = _undoStack.Take(MaxUndo).Reverse().ToArray();
                _undoStack.Clear();
                foreach (var s in keep)
                    _undoStack.Push(s);
            }

            _redoStack.Clear();
            UpdateUndoButtons();
        }

        private void UpdateUndoButtons()
        {
            BtnUndo.IsEnabled = _undoStack.Count > 1;
            BtnRedo.IsEnabled = _redoStack.Count > 0;
        }

        private void UndoDoc()
        {
            if (_undoStack.Count <= 1)
                return;
            _snapTimer.Stop();
            _redoStack.Push(_undoStack.Pop());
            RestoreSnapshot(_undoStack.Peek());
        }

        private void RedoDoc()
        {
            if (_redoStack.Count == 0)
                return;
            _snapTimer.Stop();
            var s = _redoStack.Pop();
            _undoStack.Push(s);
            RestoreSnapshot(s);
        }

        private void RestoreSnapshot(JsonObject snap)
        {
            _restoring = true;
            try
            {
                var doc = PaperBox.Document;
                int caret = 0;
                try
                {
                    caret = doc.ContentStart.GetOffsetToPosition(PaperBox.CaretPosition);
                }
                catch { }

                LoadV2(snap);

                try
                {
                    int max = doc.ContentStart.GetOffsetToPosition(doc.ContentEnd);
                    var tp = doc.ContentStart.GetPositionAtOffset(
                        Math.Clamp(caret, 0, Math.Max(0, max)));
                    if (tp != null)
                        PaperBox.CaretPosition = tp;
                }
                catch { }

                PaperBox.Focus();
            }
            finally
            {
                _restoring = false;
            }
            UpdateUndoButtons();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            UndoDoc();
            PaperBox.Focus();
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            RedoDoc();
            PaperBox.Focus();
        }

        private void PaperBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !e.IsRepeat)
            {
                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (e.Key == Key.Z && !shift)
                {
                    UndoDoc();
                    e.Handled = true;
                }
                else if (e.Key == Key.Y || (e.Key == Key.Z && shift))
                {
                    RedoDoc();
                    e.Handled = true;
                }
            }
        }

        // ============================================================
        // ПУБЛИЧНОЕ API ДЛЯ ГЛАВНОГО ОКНА
        // ============================================================

        public PreparedImage BuildPrintImage(PrintBitOrder order)
        {
            var (black, w, h) = RenderPaperBool();
            byte[] raster = RasterConverter.Pack(black, w, h, order);
            BitmapSource preview = RasterConverter.ToPreview(black, w, h);
            return new PreparedImage(preview, raster, w, h);
        }

        public void NewDocument()
        {
            PaperBox.Document.Blocks.Clear();
            PaperBox.Document.Blocks.Add(new Paragraph(new Run("")));
            PaperBox.Focus();
            OnDocChanged();
            ResetUndo();
        }

        public void LoadFrom(string path)
        {
            LoadV2(JsonNode.Parse(File.ReadAllText(path))!.AsObject());
            ResetUndo();
        }

        private void ResetUndo()
        {
            _snapTimer.Stop();
            _undoStack.Clear();
            _redoStack.Clear();
            PushChange();
        }

        public void SaveTo(string path) =>
            File.WriteAllText(path, SaveV2().ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        // ============================================================
        // СТАРТОВЫЙ ДОКУМЕНТ
        // ============================================================

        private void BuildDefaultDoc()
        {
            var doc = PaperBox.Document;
            doc.Blocks.Clear();
            doc.Blocks.Add(MakePara("Заголовок", 38, TextAlignment.Center, true));
            doc.Blocks.Add(MakePara("Эта строка — слева", 24, TextAlignment.Left));
            doc.Blocks.Add(MakePara("а эта — справа ★", 24, TextAlignment.Right));
            doc.Blocks.Add(MakeDivider(DividerStyle.Solid));
            doc.Blocks.Add(MakePara("Пишите прямо здесь. Win+. — эмодзи 🙂", 20, TextAlignment.Left));
        }

        private static Paragraph MakePara(
            string text, double size, TextAlignment align, bool bold = false)
        {
            var run = new Run(text);
            if (bold)
                run.FontWeight = FontWeights.Bold;
            return new Paragraph(run)
            {
                FontSize = size,
                TextAlignment = align
            };
        }

        // ============================================================
        // ВСТАВКА: КАРТИНКА / ЭМОДЗИ / РАЗДЕЛИТЕЛЬ / ОТСТУП
        // ============================================================

        private Block CaretBlock() =>
            (Block?)PaperBox.CaretPosition.Paragraph
            ?? PaperBox.Document.Blocks.FirstBlock
            ?? new Paragraph();

        private void InsertAfterCaret(Block block)
        {
            var caret = CaretBlock();
            if (caret != PaperBox.Document.Blocks.FirstBlock ||
                PaperBox.Document.Blocks.Contains(caret))
            {
                try
                {
                    caret.SiblingBlocks.InsertAfter(caret, block);
                    return;
                }
                catch { }
            }
            PaperBox.Document.Blocks.Add(block);
        }

        private void BtnAddImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Картинка в документ",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true)
                return;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                // Ограничиваем оригинал для компактного .catdoc
                BitmapSource stored = bmp.PixelWidth > 768
                    ? (BitmapSource)new TransformedBitmap(
                        bmp, new ScaleTransform(
                            768.0 / bmp.PixelWidth, 768.0 / bmp.PixelWidth))
                    : bmp;

                var settings = new ImageSettingsDialog(stored) { Owner = Window.GetWindow(this) };
                if (settings.ShowDialog() != true || settings.ResultPreview == null)
                    return;

                var payload = new ImagePayload
                {
                    Options = settings.ResultOptions,
                    Frame = settings.ResultFrame,
                    Rotation = settings.ResultRotation,
                    Mirror = settings.ResultMirror,
                    CropL = settings.ResultCropL,
                    CropT = settings.ResultCropT,
                    CropR = settings.ResultCropR,
                    CropB = settings.ResultCropB
                };
                payload.SetOriginal(stored);
                payload.Refresh();

                var container = MakeImagePanel(payload);
                InsertAfterCaret(container);
                OnDocChanged();
                PushChange();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Картинка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================
        // ДАБЛКЛИК ПО ВСТАВКАМ — через Preview: RichTextBox глотает
        // обычные клики на встроенных элементах, туннельное событие надёжно.
        // ============================================================

        private void PaperBox_PreviewDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2)
                return;

            try
            {
                // Цель ищем через позицию в документе, а не через OriginalSource:
                // там может быть FlowDocument/Run — не визуальные элементы,
                // VisualTreeHelper на них падает.
                TextPointer? pos = null;
                try
                {
                    pos = PaperBox.GetPositionFromPoint(
                        e.GetPosition(PaperBox), true);
                }
                catch
                {
                    return;
                }

                switch (pos?.Parent)
                {
                    case BlockUIContainer bc:
                        HandleBlockContainer(bc);
                        e.Handled = true;
                        break;

                    case InlineUIContainer iu
                        when iu.Child is Image im && im.Tag is ImagePayload:
                        OpenImageSettings(im);
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Двойной клик",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Картинка на холсте в панели-метке (Tag = "img").
        /// </summary>
        private static BlockUIContainer MakeImagePanel(ImagePayload payload)
        {
            var img = new Image
            {
                Source = payload.PreviewBW,
                Width = ImageWidth,
                Stretch = Stretch.None,
                Tag = payload,
                ToolTip = "Двойной клик — настройки картинки"
            };

            var panel = new StackPanel { Tag = "img" };
            panel.Children.Add(img);

            return new BlockUIContainer(panel);
        }

        private static Image? PanelImage(StackPanel panel) =>
            panel.Children.Count > 0 && panel.Children[0] is Image img
                ? img : null;

        /// <summary>QR на холсте: текст + размер живут в Tag. Как мобильный DocBlock.Qr.</summary>
        private sealed class QrPayload
        {
            public string Text { get; set; } = string.Empty;
            public int Size { get; set; } = 1;
        }

        private static BitmapSource? QrBitmap(QrPayload payload)
        {
            var black = QrRender.Render(
                payload.Text, QrRender.TargetPx(payload.Size));
            if (black == null)
                return null;
            return RasterConverter.ToPreview(
                black, black.GetLength(1), black.GetLength(0));
        }

        private static BlockUIContainer MakeQrPanel(QrPayload payload)
        {
            var bmp = QrBitmap(payload);
            var img = new Image
            {
                Source = bmp,
                Width = (bmp?.PixelWidth).GetValueOrDefault(192),
                HorizontalAlignment = HorizontalAlignment.Center,
                Stretch = Stretch.None,
                Tag = payload,
                ToolTip = "Двойной клик — изменить QR"
            };

            var panel = new StackPanel { Tag = "qr" };
            panel.Children.Add(img);

            return new BlockUIContainer(panel);
        }

        private void BtnAddQr_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new QrDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true)
                return;
            if (string.IsNullOrWhiteSpace(dlg.QrText))
                return;
            InsertAfterCaret(MakeQrPanel(new QrPayload
            {
                Text = dlg.QrText,
                Size = dlg.QrSize
            }));
            OnDocChanged();
            PushChange();
        }

        private void OpenQrSettings(Image img)
        {
            if (img.Tag is not QrPayload payload)
                return;
            var dlg = new QrDialog(payload.Text, payload.Size)
            {
                Owner = Window.GetWindow(this)
            };
            if (dlg.ShowDialog() != true)
                return;
            if (string.IsNullOrWhiteSpace(dlg.QrText))
                return;
            payload.Text = dlg.QrText;
            payload.Size = dlg.QrSize;
            var bmp = QrBitmap(payload);
            img.Source = bmp;
            img.Width = (bmp?.PixelWidth).GetValueOrDefault(192);
            OnDocChanged();
            PushChange();
        }

        private void HandleBlockContainer(BlockUIContainer bc)
        {
            switch (bc.Child)
            {
                case StackPanel panel when (panel.Tag as string) == "img":
                    if (PanelImage(panel) is Image im)
                        OpenImageSettings(im);
                    break;

                case StackPanel qrpanel when (qrpanel.Tag as string) == "qr":
                    if (PanelImage(qrpanel) is Image qim)
                        OpenQrSettings(qim);
                    break;

                case Line:
                    CycleDivider(bc);
                    break;

                case StackPanel:
                    CycleDivider(bc);
                    break;

                case Rectangle r when (r.Tag as string) == "gap":
                    CycleGap(r);
                    break;
            }
        }

        private void OpenImageSettings(Image img)
        {
            if (img.Tag is not ImagePayload payload)
                return;
            var original = payload.Original;
            if (original == null)
                return;

            // Снапшот для честной «Отмены»
            ImageOptions backup = CloneOptions(payload.Options);
            int backupFrame = payload.Frame;
            int backupRotation = payload.Rotation;
            bool backupMirror = payload.Mirror;
            double backupCropL = payload.CropL;
            double backupCropT = payload.CropT;
            double backupCropR = payload.CropR;
            double backupCropB = payload.CropB;

            var dlg = new ImageSettingsDialog(original, payload.Options, payload)
            {
                Owner = Window.GetWindow(this)
            };

            // Живое применение: холст, подпись и 1-бит предпросмотр
            // обновляются прямо во время кручения настроек.
            // В стек Undo точка встанет только по ОК.
            dlg.OptionsChanged += live =>
            {
                CopyOptions(live, payload.Options);
                payload.Refresh();
                img.Source = payload.PreviewBW;
                OnDocChanged();
            };

            bool? ok = dlg.ShowDialog();

            if (ok != true)
            {
                payload.Options = backup;
                payload.Frame = backupFrame;
                payload.Rotation = backupRotation;
                payload.Mirror = backupMirror;
                payload.CropL = backupCropL;
                payload.CropT = backupCropT;
                payload.CropR = backupCropR;
                payload.CropB = backupCropB;
                payload.Refresh();
                img.Source = payload.PreviewBW;
                OnDocChanged();
                return;
            }

            PushChange();
        }

        private static ImageOptions CloneOptions(ImageOptions o) => new()
        {
            Dither = o.Dither,
            Threshold = o.Threshold,
            Brightness = o.Brightness,
            Contrast = o.Contrast,
            Saturation = o.Saturation,
            Gamma = o.Gamma,
            Invert = o.Invert,
            BitOrder = o.BitOrder
        };

        private static void CopyOptions(ImageOptions from, ImageOptions to)
        {
            to.Dither = from.Dither;
            to.Threshold = from.Threshold;
            to.Brightness = from.Brightness;
            to.Contrast = from.Contrast;
            to.Saturation = from.Saturation;
            to.Gamma = from.Gamma;
            to.Invert = from.Invert;
            to.BitOrder = from.BitOrder;
        }

        private void CycleDivider(BlockUIContainer container)
        {
            int cur = 0;
            switch (container.Child)
            {
                case Line l when l.Tag is int t:
                    cur = t;
                    break;
                case StackPanel sp when (sp.Tag as string) == "div" &&
                    sp.Children.Count > 0 &&
                    sp.Children[0] is Line first && first.Tag is int t2:
                    cur = t2;
                    break;
            }
            SetDividerChild(container, (DividerStyle)((cur + 1) % 4));
            OnDocChanged();
            PushChange();
        }

        private void CycleGap(Rectangle rect)
        {
            double[] steps = { 24, 48, 80, 120 };
            int i = Array.FindIndex(steps, v => Math.Abs(v - rect.Height) < 1);
            rect.Height = steps[(i + 1) % steps.Length];
            OnDocChanged();
            PushChange();
        }

        private void InsertEmoji(string emoji)
        {
            EmojiPopup.IsOpen = false;
            PushRecentEmoji(emoji);

            try
            {
                PaperBox.CaretPosition.InsertTextInRun(emoji);
                PaperBox.Focus();
            }
            catch { }
        }

        private void PushRecentEmoji(string emoji)
        {
            if (Settings == null || string.IsNullOrWhiteSpace(emoji))
                return;

            Settings.RecentEmojis.Remove(emoji);
            Settings.RecentEmojis.Insert(0, emoji);
            while (Settings.RecentEmojis.Count > 16)
                Settings.RecentEmojis.RemoveAt(Settings.RecentEmojis.Count - 1);
            Settings.Save();
        }

        private void BtnEmoji_Click(object sender, RoutedEventArgs e)
        {
            if (Settings != null)
                Picker.SetRecents(Settings.RecentEmojis);
            EmojiPopup.PlacementTarget = BtnEmoji;
            EmojiPopup.IsOpen = true;
        }

        private static void ApplyDash(Line line, DividerStyle style)
        {
            line.StrokeDashArray = style switch
            {
                DividerStyle.Dashed => new DoubleCollection(new double[] { 12, 8 }),
                DividerStyle.Dotted => new DoubleCollection(new double[] { 2, 6 }),
                _ => null
            };
            line.StrokeThickness = 2;
        }

        private static BlockUIContainer MakeDivider(DividerStyle style)
        {
            var container = new BlockUIContainer();
            SetDividerChild(container, style);
            return container;
        }

        private static void SetDividerChild(BlockUIContainer container, DividerStyle style)
        {
            FrameworkElement child;
            if (style == DividerStyle.Double)
            {
                var stack = new StackPanel { Tag = "div" };
                var l1 = new Line
                {
                    X1 = 8, X2 = PaperWidth - 8, Stroke = Brushes.Black,
                    StrokeThickness = 2, Tag = (int)style, Margin = new Thickness(0, 6, 0, 0)
                };
                var l2 = new Line
                {
                    X1 = 8, X2 = PaperWidth - 8, Stroke = Brushes.Black,
                    StrokeThickness = 2, Tag = (int)style, Margin = new Thickness(0, 4, 0, 6)
                };
                l1.ToolTip = "Двойной клик — сменить стиль";
                l2.ToolTip = "Двойной клик — сменить стиль";
                stack.Children.Add(l1);
                stack.Children.Add(l2);
                child = stack;
            }
            else
            {
                var line = new Line
                {
                    X1 = 8, X2 = PaperWidth - 8, Stroke = Brushes.Black,
                    Tag = (int)style, Margin = new Thickness(0, 8, 0, 8),
                    ToolTip = "Двойной клик — сменить стиль"
                };
                ApplyDash(line, style);
                child = line;
            }
            container.Child = child;
        }

        private void BtnAddDivider_Click(object sender, RoutedEventArgs e)
        {
            InsertAfterCaret(MakeDivider(DividerStyle.Solid));
            OnDocChanged();
            PushChange();
        }

        private void BtnAddGap_Click(object sender, RoutedEventArgs e)
        {
            var rect = new Rectangle
            {
                Width = ContentWidth,
                Height = 48,
                Fill = Brushes.Transparent,
                Tag = "gap",
                ToolTip = "Двойной клик — сменить высоту"
            };
            InsertAfterCaret(new BlockUIContainer(rect));
            OnDocChanged();
            PushChange();
        }

        /// <summary>Пачка блоков подряд с сохранением порядка.</summary>
        private void InsertSequence(IEnumerable<Block> blocks)
        {
            Block anchor = CaretBlock();
            foreach (var block in blocks)
            {
                try
                {
                    if (PaperBox.Document.Blocks.Contains(anchor))
                        anchor.SiblingBlocks.InsertAfter(anchor, block);
                    else
                        PaperBox.Document.Blocks.Add(block);
                }
                catch
                {
                    PaperBox.Document.Blocks.Add(block);
                }
                anchor = block;
            }
            OnDocChanged();
            PushChange();
        }

        /// <summary>Шаблоны как на мобильном: готовые связки абзацев.</summary>
        private void BtnTplChecklist_Click(object sender, RoutedEventArgs e)
        {
            var blocks = new List<Block>
            {
                MakePara("Покупки", 30, TextAlignment.Center, true),
                MakeDivider(DividerStyle.Solid)
            };
            for (int i = 0; i < 5; i++)
                blocks.Add(MakePara("☐ ", 24, TextAlignment.Left));
            InsertSequence(blocks);
        }

        private void BtnTplNote_Click(object sender, RoutedEventArgs e)
        {
            InsertSequence(new Block[]
            {
                MakePara("Заметка", 30, TextAlignment.Center, true),
                MakeDivider(DividerStyle.Dashed),
                MakePara("Текст...", 24, TextAlignment.Left)
            });
        }

        private void BtnTplHeader_Click(object sender, RoutedEventArgs e)
        {
            InsertSequence(new Block[]
            {
                MakePara("Заголовок", 48, TextAlignment.Center, true)
            });
        }

        // ============================================================
        // ФОРМАТИРОВАНИЕ ВЫДЕЛЕНИЯ
        // ============================================================

        private void ApplyToSelection(DependencyProperty prop, object? value) =>
            // ВАЖНО: только живое Selection! Отделённый new TextRange на
            // ПУСТОМ диапазоне молча ничего не делает (ни вооружения,
            // ни сброса пружины) — проверено пробой.
            PaperBox.Selection.ApplyPropertyValue(prop, value);

        private void FmtFont_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbFont.SelectedItem is not string f)
                return;

            // Разделитель выбирать нельзя
            if (f.StartsWith("─"))
            {
                CmbFont.SelectedItem = _lastFont;
                return;
            }

            _lastFont = f;
            ApplyToSelection(TextElement.FontFamilyProperty,
                new FontFamily($"{f}, Segoe UI Emoji, Segoe UI Symbol"));
            // Диапазон покрасили — дальше пишем базовым шрифтом абзаца.
            ResetCaretSpring(TextElement.FontFamilyProperty, ParaFont());
            PushRecentFont(f);
            PushChange();
            // Отложенно: попап комбобокса ещё закрывается, прямой Focus
            // теряется в лимбе — печать после выбора уходит в никуда.
            FocusPaperLater();
        }

        private string _lastFont = "Segoe UI";

        /// <summary>5 последних шрифтов сверху списка.</summary>
        private void RefreshFontList(string selected)
        {
            bool wasSyncing = _syncing;
            _syncing = true;
            try
            {
                var all = Fonts.SystemFontFamilies
                    .Select(f => f.Source).OrderBy(s => s).ToList();

                var items = new List<string>();
                var recents = Settings?.RecentFonts
                    .Where(all.Contains).Distinct().Take(5).ToList()
                    ?? new List<string>();

                items.AddRange(recents);
                if (recents.Count > 0)
                    items.Add("──────────────────");
                items.AddRange(all);

                CmbFont.ItemsSource = items;
                CmbFont.SelectedItem = items.Contains(selected) ? selected : "Segoe UI";
                _lastFont = CmbFont.SelectedItem as string ?? "Segoe UI";
            }
            finally
            {
                _syncing = wasSyncing;
            }
        }

        private void PushRecentFont(string font)
        {
            if (Settings == null)
                return;

            Settings.RecentFonts.Remove(font);
            Settings.RecentFonts.Insert(0, font);
            while (Settings.RecentFonts.Count > 5)
                Settings.RecentFonts.RemoveAt(Settings.RecentFonts.Count - 1);
            Settings.Save();

            string current = CmbFont.SelectedItem as string ?? font;
            RefreshFontList(current);
        }

        private double ParseSize()
        {
            string s = CmbSize.SelectedItem as string ?? CmbSize.Text ?? "24";
            if (double.TryParse(s, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double v) ||
                double.TryParse(s, out v))
                return Math.Clamp(v, 8, 96);
            return 24;
        }

        private void FmtSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;
            ApplyToSelection(TextElement.FontSizeProperty, ParseSize());
            // Диапазон покрасили — дальше пишем базовым кеглем абзаца.
            ResetCaretSpring(TextElement.FontSizeProperty, ParaSize());
            PushChange();
            FocusPaperLater();
        }

        private void FmtSize_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncing)
                return;
            double v = ParseSize();
            // Пустой чих на блюре: значение не менялось — выделение не трогаем.
            object cur = new TextRange(
                PaperBox.Selection.Start, PaperBox.Selection.End)
                .GetPropertyValue(TextElement.FontSizeProperty);
            if (cur is double d && Math.Abs(d - v) < 0.5)
                return;
            ApplyToSelection(TextElement.FontSizeProperty, v);
            ResetCaretSpring(TextElement.FontSizeProperty, ParaSize());
            PushChange();
            // Фокус не дёргаем: ушли табом/мышью в другое место осознанно.
        }

        /// <summary>
        /// Фокус в бумагу после закрытия попапа комбобокса: прямой вызов
        /// из SelectionChanged теряется, пока попап сворачивается.
        /// </summary>
        private void FocusPaperLater()
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => { if (IsLoaded) PaperBox.Focus(); }));
        }

        /// <summary>Базовый шрифт абзаца под кареткой (для сброса пружины).</summary>
        private FontFamily ParaFont() =>
            (PaperBox.CaretPosition.Paragraph?.GetValue(TextElement.FontFamilyProperty)
                as FontFamily) ?? new FontFamily("Segoe UI");

        /// <summary>Базовый кегль абзаца под кареткой (для сброса пружины).</summary>
        private double ParaSize()
        {
            object v = PaperBox.CaretPosition.Paragraph
                ?.GetValue(TextElement.FontSizeProperty) ?? double.NaN;
            double d = v is double x ? x : double.NaN;
            return double.IsNaN(d) ? PaperBox.FontSize : d;
        }

        private static bool IsBold(object? v) =>
            v is FontWeight w && w == FontWeights.Bold;

        private void FmtBold_Click(object sender, RoutedEventArgs e)
        {
            bool on = TogB.IsChecked == true;
            ApplyToSelection(TextElement.FontWeightProperty,
                on ? FontWeights.Bold : FontWeights.Regular);
            // Диапазон покрасили — дальше пишем обычным (как на мобильном).
            ResetCaretSpring(TextElement.FontWeightProperty, FontWeights.Regular);
            PushChange();
            // Без возврата фокуса вооружённый стиль слетает при первом
            // клике в текст, а печать идёт вообще не в бумагу.
            PaperBox.Focus();
        }

        private void FmtItalic_Click(object sender, RoutedEventArgs e)
        {
            bool on = TogI.IsChecked == true;
            ApplyToSelection(TextElement.FontStyleProperty,
                on ? FontStyles.Italic : FontStyles.Normal);
            ResetCaretSpring(TextElement.FontStyleProperty, FontStyles.Normal);
            PushChange();
            PaperBox.Focus();
        }

        private void FmtUnderline_Click(object sender, RoutedEventArgs e)
        {
            bool on = TogU.IsChecked == true;
            ApplyToSelection(Inline.TextDecorationsProperty,
                on ? TextDecorations.Underline : null);
            ResetCaretSpring(Inline.TextDecorationsProperty, null);
            PushChange();
            PaperBox.Focus();
        }

        /// <summary>
        /// После стилизации диапазона каретка встаёт в его конец с БАЗОВЫМ
        /// значением атрибута: дальше пишется обычным. Пустое выделение
        /// (вооружение стиля для нового текста) не трогаем.
        /// </summary>
        private void ResetCaretSpring(DependencyProperty prop, object? baseValue)
        {
            var sel = PaperBox.Selection;
            if (sel.IsEmpty)
                return;
            var end = sel.End;
            PaperBox.Selection.Select(end, end);
            // Живое Selection (см. ApplyToSelection): отделённый диапазон
            // пружину не сбросит.
            PaperBox.Selection.ApplyPropertyValue(prop, baseValue);
            // Перепривязка на шаг вперёд в том же абзаце: та же визуальная
            // точка, но печать пойдёт в следующий (обычный) ран, а не в
            // только что покрашенный. Проверено пробой.
            var fwd = end.GetNextInsertionPosition(LogicalDirection.Forward);
            if (fwd != null && fwd.Paragraph == end.Paragraph)
                PaperBox.Selection.Select(fwd, fwd);
        }

        private void Align_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton btn)
                return;
            switch (btn.Tag as string)
            {
                case "0": EditingCommands.AlignLeft.Execute(null, PaperBox); break;
                case "2": EditingCommands.AlignRight.Execute(null, PaperBox); break;
                default: EditingCommands.AlignCenter.Execute(null, PaperBox); break;
            }
            PaperBox.Focus();
            PushChange();
        }

        private IEnumerable<Paragraph> SelectedParagraphs()
        {
            var sel = PaperBox.Selection;
            var start = sel.Start.Paragraph;
            var end = sel.End.Paragraph;
            if (start == null)
                yield break;

            bool inside = false;
            foreach (var block in PaperBox.Document.Blocks)
            {
                if (block is not Paragraph p)
                    continue;
                if (block == start) inside = true;
                if (inside) yield return p;
                if (block == end) yield break;
            }
        }

        private void Spacing_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbSpacing.SelectedItem is not ComboBoxItem item)
                return;
            if (!double.TryParse(item.Tag as string, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double factor))
                return;

            foreach (var p in SelectedParagraphs())
            {
                double size = (double)p.GetValue(TextElement.FontSizeProperty);
                p.LineHeight = size * factor;
                p.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            OnDocChanged();
            PushChange();
            FocusPaperLater();
        }

        /// <summary>
        /// Рамка абзаца: порядок пунктов = коды FrameStyle. Хранится в Tag.
        /// </summary>
        private void CmbFrame_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || CmbFrame.SelectedIndex < 0)
                return;
            int f = FrameStyle.Sanitize(CmbFrame.SelectedIndex);
            bool any = false;
            foreach (var p in SelectedParagraphs())
            {
                p.Tag = f == FrameStyle.None ? null : f;
                any = true;
            }
            if (any)
            {
                OnDocChanged();
                PushChange();
                FocusPaperLater();
            }
        }

        // ============================================================
        // СИНХРОНИЗАЦИЯ ПАНЕЛИ С КАРЕТКОЙ
        // ============================================================

        private void PaperBox_SelectionChanged(object sender, RoutedEventArgs e) =>
            SyncToolbar();

        private void SyncToolbar()
        {
            _syncing = true;
            try
            {
                var sel = new TextRange(
                    PaperBox.Selection.Start, PaperBox.Selection.End);

                object w = sel.GetPropertyValue(TextElement.FontWeightProperty);
                object s = sel.GetPropertyValue(TextElement.FontStyleProperty);
                object u = sel.GetPropertyValue(Inline.TextDecorationsProperty);
                object f = sel.GetPropertyValue(TextElement.FontFamilyProperty);
                object size = sel.GetPropertyValue(TextElement.FontSizeProperty);

                TogB.IsChecked = IsBold(w);
                TogI.IsChecked = s is FontStyle st && st == FontStyles.Italic;
                TogU.IsChecked = u is TextDecorationCollection tdc && tdc.Count > 0;

                if (f is FontFamily ff)
                {
                    string first = ff.Source.Split(',')[0].Trim();
                    if ((CmbFont.SelectedItem as string) != first &&
                        (CmbFont.ItemsSource as List<string>)?.Contains(first) == true)
                    {
                        CmbFont.SelectedItem = first;
                        _lastFont = first;
                    }
                }

                if (size is double d && !double.IsNaN(d))
                {
                    string str = ((int)Math.Round(d)).ToString();
                    if ((CmbSize.SelectedItem as string) != str)
                    {
                        CmbSize.SelectedItem = str;
                        CmbSize.Text = str;
                    }
                }

                var para = PaperBox.CaretPosition.Paragraph;
                if (para != null)
                {
                    AlL.IsChecked = para.TextAlignment == TextAlignment.Left;
                    AlC.IsChecked = para.TextAlignment == TextAlignment.Center;
                    AlR.IsChecked = para.TextAlignment == TextAlignment.Right;

                    int pf = para.Tag is int t ? FrameStyle.Sanitize(t) : FrameStyle.None;
                    if (CmbFrame.SelectedIndex != pf)
                        CmbFrame.SelectedIndex = pf;

                    double ps = (double)para.GetValue(TextElement.FontSizeProperty);
                    double lh = para.LineHeight;
                    double factor = double.IsNaN(lh) || ps <= 0 ? 1.25 : lh / ps;
                    int idx = factor switch
                    {
                        < 1.12 => 0,
                        < 1.37 => 1,
                        < 1.75 => 2,
                        _ => 3
                    };
                    CmbSpacing.SelectedIndex = idx;
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        // ============================================================
        // ИЗМЕНЕНИЯ / ИНФО / МАСШТАБ / 1-БИТ
        // ============================================================

        private void PaperBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OnDocChanged();

            // Печать/ввод — снапшот с задержкой, чтобы не плодить точки на каждую букву
            if (_ready && !_restoring)
            {
                _snapTimer.Stop();
                _snapTimer.Start();
            }
        }

        private void OnDocChanged()
        {
            if (!_ready)
                return;

            UpdateInfo();

            UpdateUndoButtons();
            _debTimer.Stop();
            if (ChkBit.IsChecked == true)
                _debTimer.Start();
            DocumentChanged?.Invoke();
        }

        private void UpdateInfo()
        {
            int paras = PaperBox.Document.Blocks.OfType<Paragraph>().Count();
            int pics = CountImages();
            TxtDocInfo.Text = ChkBit.IsChecked == true
                ? TxtDocInfo.Text
                : $"Абзацев: {paras} • Картинок: {pics}";
        }

        private int CountImages()
        {
            int n = 0;
            foreach (var b in PaperBox.Document.Blocks)
                if (b is BlockUIContainer c && c.Child is Image)
                    n++;
            return n;
        }

        private void SldZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PaperScale == null || TxtZoom == null)
                return;
            PaperScale.ScaleX = e.NewValue;
            PaperScale.ScaleY = e.NewValue;
            TxtZoom.Text = $"{(int)(e.NewValue * 100)}%";
        }

        private void ChkBit_Changed(object sender, RoutedEventArgs e)
        {
            bool on = ChkBit.IsChecked == true;
            PaperBox.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            BitPreview.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
                RefreshBitPreview();
            else
                UpdateInfo();
        }

        private void RefreshBitPreview()
        {
            try
            {
                var (black, w, h) = RenderPaperBool();
                BitPreview.Source = RasterConverter.ToPreview(black, w, h);
                TxtDocInfo.Text =
                    $"{w}×{h}px • ~{h / 8.0:0} мм • {(w / 8) * h} байт";
            }
            catch (Exception ex)
            {
                BitPreview.Source = null;
                TxtDocInfo.Text = ex.Message;
            }
        }

        /// <summary>
        /// Сохранить точный 1-битный растр (тот же, что уходит в печать) в PNG.
        /// </summary>
        private void BtnSaveRaster_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var img = BuildPrintImage(
                    Settings?.BitOrder ?? PrintBitOrder.LsbFirst);

                var dlg = new SaveFileDialog
                {
                    Title = "Сохранить растр",
                    Filter = "PNG|*.png",
                    DefaultExt = ".png",
                    FileName = "raster_384x" + img.Height
                };
                if (dlg.ShowDialog() != true)
                    return;

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img.Preview));
                using var fs = File.Create(dlg.FileName);
                enc.Save(fs);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Растр",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================
        // РЕНДЕР ЖИВОГО ХОЛСТА В 1 БИТ
        // ============================================================

        private (bool[,] Black, int W, int H) RenderPaperBool()
        {
            if (IsDocumentEmpty())
                throw new InvalidOperationException("Документ пуст.");

            var doc = PaperBox.Document;

            // Сохраняем выделение (подсветка иначе попадёт в печать)
            int offS = doc.ContentStart.GetOffsetToPosition(PaperBox.Selection.Start);
            int offE = doc.ContentStart.GetOffsetToPosition(PaperBox.Selection.End);
            bool wasFocused = PaperBox.IsFocused;

            // Снимаем масштаб, иначе растр поплывёт
            double zx = PaperScale.ScaleX, zy = PaperScale.ScaleY;

            // Предпросмотр прячет бумагу (Collapsed) — на время рендера показываем,
            // иначе Measure даст ноль и выйдет пустой лист.
            Visibility oldVis = PaperBox.Visibility;

            try
            {
                PaperBox.Visibility = Visibility.Visible;
                PaperBox.Selection.Select(doc.ContentStart, doc.ContentStart);
                PaperScale.ScaleX = 1;
                PaperScale.ScaleY = 1;
                PaperBox.UpdateLayout();

                PaperBox.Measure(new Size(PaperWidth, double.PositiveInfinity));
                int h = Math.Max(1, (int)Math.Ceiling(PaperBox.DesiredSize.Height));

                if (h > MaxPrintHeight)
                    throw new InvalidOperationException(
                        $"Документ слишком длинный ({h}px). Максимум {MaxPrintHeight}px.");

                PaperBox.Arrange(new Rect(0, 0, PaperWidth, h));
                PaperBox.UpdateLayout();

                var raw = new RenderTargetBitmap(
                    (int)PaperWidth, h, 96, 96, PixelFormats.Pbgra32);
                raw.Render(PaperBox);

                // Кладём на белый фон (прозрачность ≠ чёрный)
                var visual = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    ctx.DrawRectangle(Brushes.White, null,
                        new Rect(0, 0, PaperWidth, h));
                    ctx.DrawImage(raw, new Rect(0, 0, PaperWidth, h));
                }
                var flat = new RenderTargetBitmap(
                    (int)PaperWidth, h, 96, 96, PixelFormats.Pbgra32);
                flat.Render(visual);
                flat.Freeze();

                int w = (int)PaperWidth;
                int stride = w * 4;
                byte[] pixels = new byte[stride * h];
                new FormatConvertedBitmap(flat, PixelFormats.Bgra32, null, 0)
                    .CopyPixels(pixels, stride, 0);

                var black = new bool[h, w];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * stride + x * 4;
                        double lum = 0.299 * pixels[i + 2]
                                   + 0.587 * pixels[i + 1]
                                   + 0.114 * pixels[i];
                        black[y, x] = lum < 128;
                    }

                // Рамки текстовых абзацев (Paragraph.Tag): коробка на всю ширину
                // по вертикали абзаца. Без рамок путь байт-в-байт как раньше.
                // Весь блок в try: замер не должен ронять печать.
                try
                {
                    ApplyTextFrames(black, w, h);
                }
                catch { }

                return (black, w, h);
            }
            finally
            {
                PaperScale.ScaleX = zx;
                PaperScale.ScaleY = zy;
                PaperBox.Visibility = oldVis;
                PaperBox.InvalidateMeasure();
                PaperBox.UpdateLayout();

                try
                {
                    PaperBox.Selection.Select(
                        doc.ContentStart.GetPositionAtOffset(offS),
                        doc.ContentStart.GetPositionAtOffset(offE));
                }
                catch { }

                if (wasFocused)
                    PaperBox.Focus();
            }
        }

        private bool IsDocumentEmpty()
        {
            var range = new TextRange(
                PaperBox.Document.ContentStart,
                PaperBox.Document.ContentEnd);
            if (!string.IsNullOrWhiteSpace(range.Text))
                return false;

            foreach (var b in PaperBox.Document.Blocks)
                if (b is BlockUIContainer)
                    return false;

            return true;
        }

        /// <summary>
        /// Рамки абзацев: для каждого Paragraph с Tag-рамкой меряем вертикаль
        /// текста и кладём маску на всю ширину (как мобильный: коробка блока).
        /// Вызывается ПОСЛЕ построения растра, до возврата. Без рамок — no-op.
        /// </summary>
        private void ApplyTextFrames(bool[,] black, int w, int h)
        {
            foreach (var b in PaperBox.Document.Blocks)
            {
                if (b is not Paragraph p)
                    continue;
                if (p.Tag is not int fi || FrameStyle.Sanitize(fi) == FrameStyle.None)
                    continue;
                int frame = FrameStyle.Sanitize(fi);
                int m = FrameStyle.Margin(frame);
                Rect r1, r2;
                try
                {
                    r1 = p.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    r2 = p.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                }
                catch
                {
                    continue;
                }
                if (r1.IsEmpty || r2.IsEmpty)
                    continue;
                int top = (int)Math.Floor(Math.Min(r1.Top, r2.Top));
                int bottom = (int)Math.Ceiling(Math.Max(r1.Bottom, r2.Bottom));
                int contentH = Math.Max(1, bottom - top);
                int boxH = contentH + m * 2;
                bool[,] mask = FrameStyle.Mask(w, boxH, frame);
                int y0 = top - m;
                for (int y = 0; y < boxH; y++)
                {
                    int yy = y0 + y;
                    if (yy < 0 || yy >= h)
                        continue;
                    for (int x = 0; x < w; x++)
                    {
                        if (mask[y, x])
                            black[yy, x] = true;
                    }
                }
            }
        }

        // ============================================================
        // ЛИНЕЙКА 384px
        // ============================================================

        /// <summary>Перерисовать линейку (например, после смены темы).</summary>
        public void RedrawRuler() => DrawRuler();

        private void DrawRuler()
        {
            Ruler.Children.Clear();

            // Цвета из темы, чтобы линейка не сливалась с фоном
            var tick = TryGetBrush("TextMuted") ?? Brushes.Gray;

            foreach (int x in new[] { 0, 48, 96, 144, 192, 240, 288, 336, 384 })
            {
                bool major = x % 96 == 0;
                Ruler.Children.Add(new Line
                {
                    X1 = x, X2 = x,
                    Y1 = major ? 8 : 13, Y2 = 20,
                    Stroke = tick,
                    StrokeThickness = major ? 1.5 : 1
                });
            }
            foreach (int x in new[] { 0, 96, 192, 288 })
            {
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = x.ToString(),
                    FontSize = 9,
                    Foreground = tick
                };
                Canvas.SetLeft(label, x + 3);
                Canvas.SetTop(label, 0);
                Ruler.Children.Add(label);
            }
        }

        private static Brush? TryGetBrush(string key)
        {
            try
            {
                if (Application.Current?.Resources[key] is Brush b)
                    return b;
            }
            catch { }
            return null;
        }

        // ============================================================
        // СОХРАНЕНИЕ V2 (.catdoc)
        // ============================================================

        private JsonObject SaveV2()
        {
            var arr = new JsonArray();
            WalkBlocks(PaperBox.Document.Blocks, arr);
            return new JsonObject
            {
                ["app"] = "CatPrint",
                ["version"] = 2,
                ["blocks"] = arr
            };
        }

        private static void WalkBlocks(BlockCollection blocks, JsonArray arr)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph p:
                        arr.Add(SaveParagraph(p));
                        break;

                    case BlockUIContainer c:
                        var node = SaveEmbedded(c.Child);
                        if (node != null)
                            arr.Add(node);
                        break;

                    case Section s:
                        WalkBlocks(s.Blocks, arr);
                        break;

                    case List list:
                        foreach (var item in list.ListItems)
                            WalkBlocks(item.Blocks, arr);
                        break;

                    case Table table:
                        foreach (var rg in table.RowGroups)
                            foreach (var row in rg.Rows)
                                foreach (var cell in row.Cells)
                                    WalkBlocks(cell.Blocks, arr);
                        break;
                }
            }
        }

        private static JsonObject SaveParagraph(Paragraph p)
        {
            var runs = new JsonArray();
            WalkInlines(p.Inlines, runs,
                false, false,
                p.FontFamily.Source, p.FontSize);

            double size = (double)p.GetValue(TextElement.FontSizeProperty);
            double? line = double.IsNaN(p.LineHeight) || size <= 0
                ? null : p.LineHeight / size;

            var o = new JsonObject
            {
                ["kind"] = "p",
                ["align"] = (int)p.TextAlignment,
                ["runs"] = runs
            };
            if (line.HasValue)
                o["line"] = line.Value;
            if (p.Tag is int f && FrameStyle.Sanitize(f) != FrameStyle.None)
                o["frame"] = FrameStyle.Sanitize(f);
            return o;
        }

        private static void WalkInlines(
            InlineCollection inlines, JsonArray runs,
            bool b, bool i, string font, double size)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run:
                        bool rb = b || run.FontWeight == FontWeights.Bold;
                        bool ri = i || run.FontStyle == FontStyles.Italic;
                        bool ru = run.TextDecorations != null &&
                                  run.TextDecorations.Count > 0;
                        runs.Add(new JsonObject
                        {
                            ["t"] = run.Text,
                            ["b"] = rb,
                            ["i"] = ri,
                            ["u"] = ru,
                            ["f"] = ((FontFamily)run.GetValue(TextElement.FontFamilyProperty)).Source,
                            ["s"] = (double)run.GetValue(TextElement.FontSizeProperty)
                        });
                        break;

                    case LineBreak:
                        runs.Add(new JsonObject
                        {
                            ["t"] = "\n", ["b"] = b, ["i"] = i,
                            ["u"] = false, ["f"] = font, ["s"] = size
                        });
                        break;

                    case Span span:
                        WalkInlines(span.Inlines, runs,
                            b || span.FontWeight == FontWeights.Bold,
                            i || span.FontStyle == FontStyles.Italic,
                            ((FontFamily)span.GetValue(TextElement.FontFamilyProperty)).Source,
                            (double)span.GetValue(TextElement.FontSizeProperty));
                        break;

                    case InlineUIContainer iu when
                        iu.Child is Image img && img.Tag is ImagePayload payload:
                        runs.Add(new JsonObject
                        {
                            ["img"] = true,
                            ["png"] = payload.PngBase64,
                            ["dither"] = (int)payload.Options.Dither,
                            ["threshold"] = payload.Options.Threshold,
                            ["brightness"] = payload.Options.Brightness,
                            ["contrast"] = payload.Options.Contrast,
                            ["saturation"] = payload.Options.Saturation,
                            ["gamma"] = payload.Options.Gamma,
                            ["invert"] = payload.Options.Invert,
                            ["rot"] = payload.Rotation,
                            ["mir"] = payload.Mirror,
                            ["frame"] = payload.Frame
                        });
                        break;
                }
            }
        }

        private static JsonObject? SaveEmbedded(UIElement? child)
        {
            // QR-панель: Tag == "qr", текст+размер в Image.Tag.
            if (child is StackPanel qr && (qr.Tag as string) == "qr")
            {
                if (PanelImage(qr) is Image qim && qim.Tag is QrPayload qp)
                {
                    return new JsonObject
                    {
                        ["kind"] = "qr",
                        ["t"] = qp.Text,
                        ["s"] = qp.Size
                    };
                }
                return null;
            }

            // Картинка теперь живёт в панели с подписью: Tag == "img"
            if (child is StackPanel panel && (panel.Tag as string) == "img")
            {
                if (PanelImage(panel) is Image img && img.Tag is ImagePayload payload)
                    return SavePayload(payload);
                return null;
            }

            switch (child)
            {
                case Image img when img.Tag is ImagePayload payload:
                    return SavePayload(payload);

                case Line line when line.Tag is int style:
                    return new JsonObject
                    {
                        ["kind"] = "div",
                        ["style"] = style
                    };

                case StackPanel stack when
                    stack.Children.Count > 0 &&
                    stack.Children[0] is Line first && first.Tag is int st:
                    return new JsonObject
                    {
                        ["kind"] = "div",
                        ["style"] = st
                    };

                case Rectangle rect when (rect.Tag as string) == "gap":
                    return new JsonObject
                    {
                        ["kind"] = "gap",
                        ["h"] = (int)rect.Height
                    };

                default:
                    return null;
            }
        }

        private static JsonObject SavePayload(ImagePayload payload) =>
            new()
            {
                ["kind"] = "img",
                ["png"] = payload.PngBase64,
                ["dither"] = (int)payload.Options.Dither,
                ["threshold"] = payload.Options.Threshold,
                ["brightness"] = payload.Options.Brightness,
                ["contrast"] = payload.Options.Contrast,
                ["saturation"] = payload.Options.Saturation,
                ["gamma"] = payload.Options.Gamma,
                ["invert"] = payload.Options.Invert,
                ["rot"] = payload.Rotation,
                ["mir"] = payload.Mirror,
                ["frame"] = payload.Frame,
                ["cropL"] = payload.CropL,
                ["cropT"] = payload.CropT,
                ["cropR"] = payload.CropR,
                ["cropB"] = payload.CropB
            };

        // ============================================================
        // ЗАГРУЗКА V2
        // ============================================================

        private void LoadV2(JsonObject root)
        {
            if (root["version"]?.GetValue<int>() != 2)
                throw new InvalidOperationException(
                    "Старый формат документа — создайте заново.");

            PaperBox.Document.Blocks.Clear();

            foreach (var node in root["blocks"]!.AsArray())
            {
                var o = node!.AsObject();
                string kind = o["kind"]?.GetValue<string>() ?? string.Empty;

                switch (kind)
                {
                    case "p":
                        PaperBox.Document.Blocks.Add(LoadParagraph(o));
                        break;

                    case "img":
                        PaperBox.Document.Blocks.Add(
                            LoadImageBlock(o));
                        break;

                    case "div":
                        PaperBox.Document.Blocks.Add(MakeDivider(
                            (DividerStyle)(o["style"]?.GetValue<int>() ?? 0)));
                        break;

                    case "qr":
                        string qt = o["t"]?.GetValue<string>() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(qt))
                        {
                            PaperBox.Document.Blocks.Add(MakeQrPanel(new QrPayload
                            {
                                Text = qt,
                                Size = o["s"]?.GetValue<int>() switch
                                {
                                    0 => 0,
                                    2 => 2,
                                    _ => 1
                                }
                            }));
                        }
                        break;

                    case "gap":
                        var rect = new Rectangle
                        {
                            Width = ContentWidth,
                            Height = o["h"]?.GetValue<int>() ?? 48,
                            Fill = Brushes.Transparent,
                            Tag = "gap",
                            ToolTip = "Двойной клик — сменить высоту"
                        };
                        PaperBox.Document.Blocks.Add(new BlockUIContainer(rect));
                        break;
                }
            }

            if (PaperBox.Document.Blocks.Count == 0)
                PaperBox.Document.Blocks.Add(new Paragraph(new Run("")));

            OnDocChanged();
        }

        private Paragraph LoadParagraph(JsonObject o)
        {
            var p = new Paragraph
            {
                TextAlignment = (TextAlignment)(o["align"]?.GetValue<int>() ?? 0)
            };

            if (o["frame"] != null)
                p.Tag = FrameStyle.Sanitize(o["frame"]!.GetValue<int>());

            if (o["line"] != null)
            {
                double factor = o["line"]!.GetValue<double>();
                p.Loaded += (s, e) =>
                {
                    double size = (double)((Paragraph)s).GetValue(
                        TextElement.FontSizeProperty);
                    ((Paragraph)s).LineHeight = size * factor;
                    ((Paragraph)s).LineStackingStrategy =
                        LineStackingStrategy.BlockLineHeight;
                };
            }

            foreach (var rnode in o["runs"]!.AsArray())
            {
                var r = rnode!.AsObject();

                if (r["img"]?.GetValue<bool>() == true)
                {
                    p.Inlines.Add(new InlineUIContainer(
                        MakeImage(r), p.ContentEnd));
                    continue;
                }

                string text = r["t"]?.GetValue<string>() ?? string.Empty;
                if (text == "\n")
                {
                    p.Inlines.Add(new LineBreak());
                    continue;
                }

                var run = new Run(text)
                {
                    FontFamily = new FontFamily(
                        r["f"]?.GetValue<string>() ?? "Segoe UI"),
                    FontSize = r["s"]?.GetValue<double>() ?? 24
                };
                if (r["b"]?.GetValue<bool>() == true)
                    run.FontWeight = FontWeights.Bold;
                if (r["i"]?.GetValue<bool>() == true)
                    run.FontStyle = FontStyles.Italic;
                if (r["u"]?.GetValue<bool>() == true)
                    run.TextDecorations = TextDecorations.Underline;

                p.Inlines.Add(run);
            }

            return p;
        }

        private BlockUIContainer LoadImageBlock(JsonObject o) =>
            MakeImagePanel(PayloadFromJson(o));

        private ImagePayload PayloadFromJson(JsonObject o)
        {
            var payload = new ImagePayload
            {
                Options = new ImageOptions
                {
                    Dither = Enum.IsDefined(typeof(DitherMode),
                        o["dither"]?.GetValue<int>() ?? 1)
                        ? (DitherMode)(o["dither"]?.GetValue<int>() ?? 1)
                        : DitherMode.FloydSteinberg,
                    Threshold = o["threshold"]?.GetValue<int>() ?? 128,
                    Brightness = o["brightness"]?.GetValue<double>() ?? 0,
                    Contrast = o["contrast"]?.GetValue<double>() ?? 0,
                    Saturation = o["saturation"]?.GetValue<double>() ?? 100,
                    Gamma = o["gamma"]?.GetValue<double>() ?? 1.0,
                    Invert = o["invert"]?.GetValue<bool>() ?? false
                },
                PngBase64 = o["png"]?.GetValue<string>() ?? string.Empty,
                Rotation = o["rot"]?.GetValue<int>() switch
                {
                    90 or 180 or 270 => o["rot"]!.GetValue<int>(),
                    _ => 0
                },
                Mirror = o["mir"]?.GetValue<bool>() ?? false,
                Frame = FrameStyle.Sanitize(o["frame"]?.GetValue<int>() ?? 0),
                CropL = o["cropL"]?.GetValue<double>() ?? 0,
                CropT = o["cropT"]?.GetValue<double>() ?? 0,
                CropR = o["cropR"]?.GetValue<double>() ?? 0,
                CropB = o["cropB"]?.GetValue<double>() ?? 0
            };
            payload.SanitizeCrop();
            payload.Refresh();
            return payload;
        }

        private Image MakeImage(JsonObject o)
        {
            var payload = PayloadFromJson(o);

            var img = new Image
            {
                Source = payload.PreviewBW,
                Width = ImageWidth,
                Stretch = Stretch.None,
                Tag = payload,
                ToolTip = "Двойной клик — настройки картинки"
            };
            return img;
        }
    }
}
