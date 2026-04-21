using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BVCC
{
    public class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double value = (double)values[0];
            double max = (double)values[1];
            double width = (double)values[2];

            if (max == 0) return 0;
            return (value / max) * width;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    public class MultiplierConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return 0.0;
            if (double.TryParse(values[0]?.ToString(), out double progress) &&
                double.TryParse(values[1]?.ToString(), out double width))
            {
                return progress * width;
            }

            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

    }
    public class ActionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var hex = value as string == "Removing" ? "#FFFF4444"
                    : value as string == "ChangingVersion" ? "#FFAAAAAA"
                    : "#FF0099FF";

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    public class IsProgressingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value as string == parameter as string;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();

    }
    public class ProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4) return 0.0;

            if (!double.TryParse(values[0]?.ToString(), out double value) ||
                !double.TryParse(values[1]?.ToString(), out double minimum) ||
                !double.TryParse(values[2]?.ToString(), out double maximum) ||
                !double.TryParse(values[3]?.ToString(), out double width))
                return 0.0;

            if (maximum <= minimum || width <= 0) return 0.0;

            double ratio = (value - minimum) / (maximum - minimum);
            return Math.Max(0.0, Math.Min(width * ratio, width));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
    public class GreaterThanZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is int count && count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}