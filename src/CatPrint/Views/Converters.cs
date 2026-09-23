using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

using CatPrint.Imaging;

namespace CatPrint.Views
{
    public sealed class AlignIntToTextAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is int a ? a : 1) switch
            {
                0 => TextAlignment.Left,
                2 => TextAlignment.Right,
                _ => TextAlignment.Center
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is TextAlignment a
                ? a switch { TextAlignment.Left => 0, TextAlignment.Right => 2, _ => 1 }
                : 1;
        }
    }

    public sealed class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? FontWeights.Bold : FontWeights.Regular;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is FontWeight w && w == FontWeights.Bold;
    }

    public sealed class BoolToFontStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? FontStyles.Italic : FontStyles.Normal;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is FontStyle s && s == FontStyles.Italic;
    }

    public sealed class DitherIntToNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int i = value is int v ? v : 1;
            return Enum.IsDefined(typeof(Imaging.DitherMode), i)
                ? ((Imaging.DitherMode)i).ToDisplayName()
                : "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
