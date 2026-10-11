using Hermes.Client.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Hermes.Client.WinUI3.Controls;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>"131 messages • openai/gpt-5.6-luna-pro • 5m ago" for a session row.</summary>
public sealed class SessionMetaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is Session s
            ? $"{s.MessageCount} messages • {s.Model ?? "no model"} • {RelativeTime.Format(s.LastActive)}"
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
