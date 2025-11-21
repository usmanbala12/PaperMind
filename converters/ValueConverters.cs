using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace PaperMind.Converters
{
    public class EmptyStringToGrayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && string.IsNullOrEmpty(str))
            {
                return new SolidColorBrush(Color.Parse("#959DA5"));
            }
            return new SolidColorBrush(Color.Parse("#24292E"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ProcessingButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isProcessing)
            {
                return isProcessing ? "⏸️ Cancel Processing" : "▶️ Start Processing";
            }
            return "▶️ Start Processing";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percent)
            {
                return percent; // Return as percentage, will be bound to Width with parent container
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LogLevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string logEntry)
            {
                if (logEntry.Contains("ERROR") || logEntry.Contains("Failed"))
                {
                    return new SolidColorBrush(Color.Parse("#FEF2F2")); // Light red
                }
                else if (logEntry.Contains("Completed") || logEntry.Contains("Success"))
                {
                    return new SolidColorBrush(Color.Parse("#F0FDF4")); // Light green
                }
                else if (logEntry.Contains("Queued") || logEntry.Contains("processing"))
                {
                    return new SolidColorBrush(Color.Parse("#EFF6FF")); // Light blue
                }
            }
            return new SolidColorBrush(Colors.White);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}