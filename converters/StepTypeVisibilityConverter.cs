using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PaperMind.Converters
{
    public class StepTypeVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string stepType && parameter is string targetStepType)
            {
                return stepType == targetStepType;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
