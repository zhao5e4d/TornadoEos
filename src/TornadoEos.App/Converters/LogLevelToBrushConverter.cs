using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TornadoEos.Core.Logging;

namespace TornadoEos.App.Converters;

/// <summary>Maps a <see cref="LogLevel"/> to a text brush for the log view.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LogLevel.Debug => new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98)),
        LogLevel.Info => new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9)),
        LogLevel.Success => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
        LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41)),
        LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)),
        _ => Brushes.Gainsboro,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
