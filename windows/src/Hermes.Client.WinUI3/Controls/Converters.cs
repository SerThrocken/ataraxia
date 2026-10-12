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

/// <summary>Inverse of <see cref="BoolToVisibilityConverter"/>.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound value is non-null (e.g. an error string).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>"Enabled • Configured • 4 tools" for a toolset row.</summary>
public sealed class ToolsetStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is ToolsetInfo t
            ? $"{(t.Enabled ? "Enabled" : "Disabled")} • {(t.Configured ? "Configured" : "Not configured")} • {t.Tools.Count} tools"
            : "";

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

/// <summary>"0 7 * * * • scheduled • next Oct 12, 7:00 AM" for a cron job row.</summary>
public sealed class CronJobMetaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is CronJob j
            ? $"{j.ScheduleDisplay ?? "no schedule"} • {j.State ?? (j.Enabled ? "enabled" : "disabled")}" +
              (j.NextRunAt is null ? "" : $" • next {j.NextRunAt.Value.ToLocalTime():MMM d, h:mm tt}")
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Run status string → theme brush: completed → success, failed → critical,
/// waiting_for_approval → caution, active states → attention, else neutral.
/// Brushes come from theme resources so high-contrast themes track automatically.
/// </summary>
/// <summary>
/// Companion-HUD status dot → theme brush: connected → success, active run →
/// caution (the amber family, like the run-status converter), unreachable →
/// critical. Theme resources only, so high-contrast themes track.
/// </summary>
public sealed class HudDotKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            Core.HudDotKind.Ok => "SystemFillColorSuccessBrush",
            Core.HudDotKind.Working => "SystemFillColorCautionBrush",
            Core.HudDotKind.Error => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorNeutralBrush",
        };
        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : Application.Current.Resources["SystemFillColorNeutralBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class RunStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            "completed" => "SystemFillColorSuccessBrush",
            "failed" => "SystemFillColorCriticalBrush",
            "waiting_for_approval" => "SystemFillColorCautionBrush",
            "running" or "stopping" or "queued" => "SystemFillColorAttentionBrush",
            _ => "SystemFillColorNeutralBrush",
        };
        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : Application.Current.Resources["SystemFillColorNeutralBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// DiagnosticStatus → theme brush: Pass → success, Warn → caution,
/// Fail → critical. Brushes come from theme resources so high-contrast
/// themes track automatically.
/// </summary>
public sealed class DiagnosticStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            Core.DiagnosticStatus.Pass => "SystemFillColorSuccessBrush",
            Core.DiagnosticStatus.Warn => "SystemFillColorCautionBrush",
            Core.DiagnosticStatus.Fail => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorNeutralBrush",
        };
        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : Application.Current.Resources["SystemFillColorNeutralBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Diff line kind to foreground brush. Theme-aware via system resources —
/// tracks light/dark/high-contrast automatically.
/// </summary>
public sealed class DiffLineKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value switch
        {
            DiffLineKind.Addition => "SystemFillColorSuccessBrush",
            DiffLineKind.Deletion => "SystemFillColorCriticalBrush",
            DiffLineKind.Other => "TextFillColorTertiaryBrush",
            _ => "TextFillColorPrimaryBrush",
        };
        return Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush
            : Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Diff line kind to its unified-diff sign prefix (+/-/space).</summary>
public sealed class DiffLineKindToSignConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            DiffLineKind.Addition => "+",
            DiffLineKind.Deletion => "-",
            DiffLineKind.Other => "~",
            _ => " ",
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
