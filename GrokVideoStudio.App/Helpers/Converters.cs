using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GrokVideoStudio.Core.Models;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.Helpers;

/// <summary>
/// Converts a VideoGenerationStatus enum to a Segoe Fluent Icon glyph.
/// </summary>
public class StatusToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VideoGenerationStatus status)
        {
            return status switch
            {
                VideoGenerationStatus.Completed => "\uE73E",
                VideoGenerationStatus.Failed => "\uE711",
                VideoGenerationStatus.Expired => "\uE7BA",
                VideoGenerationStatus.Cancelled => "\uE738",
                VideoGenerationStatus.Polling => "\uE712",
                VideoGenerationStatus.Submitting => "\uE712",
                _ => "\uE7B3"
            };
        }
        return "\uE7B3";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Converts a VideoGenerationStatus enum to a foreground brush color.
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is VideoGenerationStatus status)
        {
            return status switch
            {
                VideoGenerationStatus.Completed => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                VideoGenerationStatus.Failed => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                VideoGenerationStatus.Expired => new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0x00)),
                VideoGenerationStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0x96, 0x96, 0x96)),
                _ => new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Converts a collection count to Visibility — shows empty state when count is 0.
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Inverts StringToVisibility — visible when string is null/empty, collapsed when it has content.
/// </summary>
public class InvertStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var str = value as string;
        return string.IsNullOrEmpty(str)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Converts a LogLevel to a background brush for the activity log badge.
/// Info=gray, Warning=amber, Error=red, Debug=blue, Trace=dim blue.
/// </summary>
public class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
                LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)),
                LogLevel.Information => new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)),
                LogLevel.Debug => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
                LogLevel.Trace => new SolidColorBrush(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                _ => new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F))
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Converts a string path to a Uri for MediaElement Source binding.
/// </summary>
public class StringToUriConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
            return new Uri(path, UriKind.Absolute);
        return Binding.DoNothing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
