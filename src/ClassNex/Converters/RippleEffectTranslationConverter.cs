using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassNex.Converters;

/// <summary>1:1 移植自 CI（ClassIsland.Core/Converters/RippleEffectTranslationConverter.cs）。</summary>
public class RippleEffectTranslationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d)
        {
            return 0.0;
        }
        return -d / 2;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
