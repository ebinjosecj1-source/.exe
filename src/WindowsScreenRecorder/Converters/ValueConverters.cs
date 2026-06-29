using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WindowsScreenRecorder.Core.Enums;

namespace WindowsScreenRecorder.Converters
{
    // ─── RecordingStateToColorConverter ─────────────────────────────────────────
    // Maps the current RecordingState to a SolidColorBrush for the record button
    // and status indicator dot in the UI.

    [ValueConversion(typeof(RecordingState), typeof(Brush))]
    public sealed class RecordingStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not RecordingState state)
                return Brushes.Gray;

            return state switch
            {
                RecordingState.Recording => new SolidColorBrush(Color.FromRgb(0xE8, 0x28, 0x4F)),  // AccentRed
                RecordingState.Paused    => new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)),  // Amber
                RecordingState.Stopped   => new SolidColorBrush(Color.FromRgb(0x3D, 0x8E, 0xFF)),  // AccentBlue
                RecordingState.Countdown => new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),  // Yellow
                _                        => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),  // Disabled gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── RecordingStateToBoolConverter ──────────────────────────────────────────
    // Returns true when the state matches the ConverterParameter string.
    // Usage: Converter={StaticResource StateToBoolConverter}, ConverterParameter=Recording

    [ValueConversion(typeof(RecordingState), typeof(bool))]
    public sealed class RecordingStateToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not RecordingState state)
                return false;

            if (parameter is string paramStr &&
                Enum.TryParse<RecordingState>(paramStr, out var expected))
                return state == expected;

            // With no parameter, return true when NOT in Stopped state (i.e. active)
            return state != RecordingState.Stopped;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── BoolToVisibilityConverter ───────────────────────────────────────────────
    // true → Visible, false → Collapsed (standard pattern).

    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Visible;
    }

    // ─── InverseBoolToVisibilityConverter ───────────────────────────────────────
    // true → Collapsed, false → Visible (inverse of above).

    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Collapsed;
    }

    // ─── LevelToWidthConverter ───────────────────────────────────────────────────
    // Converts a float audio level (0.0–1.0) to a pixel Width for the VU meter bar.
    // ConverterParameter = total bar width in pixels (default 120).

    [ValueConversion(typeof(double), typeof(double))]
    public sealed class LevelToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double level = value switch
            {
                float  f => f,
                double d => d,
                _        => 0.0
            };

            double maxWidth = 120.0;
            if (parameter is string s && double.TryParse(s, out double pw))
                maxWidth = pw;

            level = Math.Clamp(level, 0.0, 1.0);
            return level * maxWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── EnumToDisplayNameConverter ─────────────────────────────────────────────
    // Formats enum values into human-readable strings by inserting spaces before
    // capital letters: "HighQuality" → "High Quality", "H264" → "H264".

    [ValueConversion(typeof(Enum), typeof(string))]
    public sealed class EnumToDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return string.Empty;

            string raw = value.ToString() ?? string.Empty;
            if (raw.Length == 0) return raw;

            // Insert space before each uppercase letter that follows a lowercase letter
            var builder = new System.Text.StringBuilder(raw.Length + 4);
            builder.Append(raw[0]);

            for (int i = 1; i < raw.Length; i++)
            {
                if (char.IsUpper(raw[i]) && char.IsLower(raw[i - 1]))
                    builder.Append(' ');
                builder.Append(raw[i]);
            }

            return builder.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── BoolToBrushConverter ────────────────────────────────────────────────────
    // Returns TrueBrush when the bound bool is true, FalseBrush otherwise.
    // Used inline in XAML for conditional border colors (e.g., error notification).

    public sealed class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush  { get; set; } = Brushes.Red;
        public Brush FalseBrush { get; set; } = Brushes.Gray;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? TrueBrush : FalseBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── NullToVisibilityConverter ───────────────────────────────────────────────
    // null → Collapsed, non-null → Visible. Useful for optional device bindings.

    [ValueConversion(typeof(object), typeof(Visibility))]
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── StringEmptyToVisibilityConverter ───────────────────────────────────────
    // Empty/null string → Collapsed, non-empty → Visible.

    [ValueConversion(typeof(string), typeof(Visibility))]
    public sealed class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── PercentageConverter ─────────────────────────────────────────────────────
    // Multiplies a double value by the ConverterParameter percentage.
    // e.g., value=200, parameter=0.5 → 100. Used for responsive width calculations.

    [ValueConversion(typeof(double), typeof(double))]
    public sealed class PercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double d) return 0.0;
            if (parameter is string s && double.TryParse(s, out double pct))
                return d * pct;
            return d;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── TimeSpanToStringConverter ───────────────────────────────────────────────
    // Formats a TimeSpan into HH:MM:SS for the recording timer display.

    [ValueConversion(typeof(TimeSpan), typeof(string))]
    public sealed class TimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan ts)
                return ts.ToString(@"hh\:mm\:ss");
            return "00:00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── FileSizeConverter ───────────────────────────────────────────────────────
    // Formats a long byte count into a human-readable size string (KB / MB / GB).

    [ValueConversion(typeof(long), typeof(string))]
    public sealed class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long bytes = value switch
            {
                long   l => l,
                int    i => (long)i,
                double d => (long)d,
                _        => 0L
            };

            return bytes switch
            {
                < 1_024             => $"{bytes} B",
                < 1_048_576         => $"{bytes / 1_024.0:F1} KB",
                < 1_073_741_824     => $"{bytes / 1_048_576.0:F1} MB",
                _                   => $"{bytes / 1_073_741_824.0:F2} GB",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
