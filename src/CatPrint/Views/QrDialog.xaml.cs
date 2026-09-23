using System;
using System.Windows;

using CatPrint.Imaging;

namespace CatPrint.Views
{
    /// <summary>
    /// QR-код: текст + размер, живое 1-битное превью 1:1.
    /// </summary>
    public partial class QrDialog : Window
    {
        public string QrText { get; private set; } = string.Empty;
        public int QrSize { get; private set; } = 1;

        public QrDialog(string text = "", int size = 1)
        {
            InitializeComponent();
            DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);

            TxtCode.Text = text ?? string.Empty;
            CmbSize.SelectedIndex = size switch { 0 => 0, 2 => 2, _ => 1 };
            TxtCode.CaretIndex = TxtCode.Text.Length;
            Update();
        }

        private void TxtCode_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!IsLoaded)
                return;
            Update();
        }

        private void CmbSize_Changed(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;
            Update();
        }

        private void Update()
        {
            if (TxtCode == null || PreviewImage == null)
                return;

            int size = CmbSize.SelectedIndex switch { 0 => 0, 2 => 2, _ => 1 };
            string text = TxtCode.Text ?? string.Empty;
            bool[,]? black = QrRender.Render(text, QrRender.TargetPx(size));
            if (black == null)
            {
                PreviewImage.Source = null;
                TxtInfo.Text = string.IsNullOrWhiteSpace(text)
                    ? "Введи текст кода."
                    : "Не влез в QR — укороти текст.";
                BtnOk.IsEnabled = false;
                return;
            }

            int w = black.GetLength(1), h = black.GetLength(0);
            PreviewImage.Source = RasterConverter.ToPreview(black, w, h);
            TxtInfo.Text = $"{w}×{h}px";
            BtnOk.IsEnabled = true;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            QrText = TxtCode.Text ?? string.Empty;
            QrSize = CmbSize.SelectedIndex switch { 0 => 0, 2 => 2, _ => 1 };
            DialogResult = true;
            Close();
        }
    }
}
