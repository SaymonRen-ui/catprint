using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CatPrint.Views
{
    public partial class EmojiPicker : UserControl
    {
        public event Action<string>? Picked;

        private List<string> _recents = new();

        public EmojiPicker()
        {
            InitializeComponent();
            Rebuild();
        }

        /// <summary>Обновить вкладку недавних (вызывать перед показом).</summary>
        public void SetRecents(List<string> recents)
        {
            _recents = recents
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct().Take(16).ToList();
            Rebuild();
        }

        private void Rebuild()
        {
            BuildCategories();
            ShowCategory(0);
        }

        private int CategoryCount =>
            (_recents.Count > 0 ? 1 : 0) + EmojiData.Categories.Count;

        private (string Title, List<string> Items) CategoryAt(int index)
        {
            if (_recents.Count > 0)
            {
                if (index == 0)
                    return ("🕘 Недавние", _recents);
                var c = EmojiData.Categories[index - 1];
                return (c.Title, c.Items);
            }
            var cat = EmojiData.Categories[index];
            return (cat.Title, cat.Items);
        }

        private void BuildCategories()
        {
            CatBar.Children.Clear();

            for (int i = 0; i < CategoryCount; i++)
            {
                var (title, _) = CategoryAt(i);
                var btn = new Button
                {
                    Content = title,
                    FontSize = 12,
                    Padding = new Thickness(9, 4, 9, 4),
                    Margin = new Thickness(0, 0, 6, 4),
                    Tag = i
                };
                btn.Click += Cat_Click;
                CatBar.Children.Add(btn);
            }
        }

        private void Cat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx)
                ShowCategory(idx);
        }

        private void ShowCategory(int index)
        {
            // Подсветка активной пилюли
            for (int i = 0; i < CatBar.Children.Count; i++)
            {
                if (CatBar.Children[i] is not Button btn)
                    continue;

                if (i == index)
                {
                    btn.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                    btn.Foreground = Brushes.White;
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                }
                else
                {
                    btn.ClearValue(Control.BackgroundProperty);
                    btn.ClearValue(Control.ForegroundProperty);
                    btn.ClearValue(Control.BorderBrushProperty);
                }
            }

            ItemsPanel.Children.Clear();

            var (_, items) = CategoryAt(index);
            var emojiFont = new FontFamily("Segoe UI Emoji, Segoe UI Symbol");

            foreach (string emoji in items)
            {
                // Как на бумаге: белый фон, чёрный глиф
                var btn = new Button
                {
                    Content = emoji,
                    FontFamily = emojiFont,
                    FontSize = 21,
                    Width = 38,
                    Height = 38,
                    Margin = new Thickness(2),
                    Padding = new Thickness(1),
                    Background = Brushes.White,
                    Foreground = Brushes.Black,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    Tag = emoji
                };
                btn.Click += (s, e) => Picked?.Invoke((string)((Button)s).Tag);
                ItemsPanel.Children.Add(btn);
            }
        }
    }
}
