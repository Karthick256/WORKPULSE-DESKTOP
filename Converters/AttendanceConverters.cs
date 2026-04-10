
using System.Globalization;
using System.Windows.Data;
using monitor_desktop.Models.Enums;

namespace monitor_desktop.Converters
{
    public class StatusToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCheckedIn)
            {
                var parts = (parameter as string)?.Split('|');
                return isCheckedIn ? parts?[0] : parts?[1];
            }
            return "INACTIVE";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCheckedIn)
            {
                var parts = (parameter as string)?.Split('|');
                return isCheckedIn ? parts?[0] : parts?[1];
            }
            return "#F87171";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SessionStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SessionStatus status)
            {
                return status switch
                {
                    SessionStatus.ACTIVE => "#34D399",
                    SessionStatus.COMPLETED => "#06B6D4",
                    SessionStatus.ABANDONED => "#F87171",
                    _ => "#94A3B8"
                };
            }
            return "#94A3B8";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MinutesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int minutes)
            {
                var hours = minutes / 60;
                var mins = minutes % 60;
                return hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";
            }
            return "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ScoreToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float score)
            {
                if (score >= 80) return "#34D399";
                if (score >= 60) return "#FBBF24";
                return "#F87171";
            }
            return "#94A3B8";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                int compareTo = parameter != null ? int.Parse(parameter.ToString()) : 0;
                return count == compareTo ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}