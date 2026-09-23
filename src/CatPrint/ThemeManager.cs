using System;
using System.Windows;

namespace CatPrint
{
    public static class ThemeManager
    {
        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static void Apply(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null)
                return;

            Current = theme;

            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    theme == AppTheme.Dark
                        ? "Themes/Dark.xaml"
                        : "Themes/Light.xaml",
                    UriKind.Relative)
            };

            var merged = app.Resources.MergedDictionaries;

            // Убираем старую тему (первый словарь, если это тема)
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                string? src = merged[i].Source?.OriginalString ?? string.Empty;
                if (src.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase))
                    merged.RemoveAt(i);
            }

            merged.Insert(0, dict);

            // Заголовки всех открытых окон под тему
            try
            {
                foreach (Window window in app.Windows)
                    Views.DarkTitleBar.Apply(window, theme == AppTheme.Dark);
            }
            catch { }
        }
    }
}
