using System;
using System.Globalization;

namespace WorkTimeCalc.Internal
{
    internal static class TimeFormatHelper
    {
        internal static readonly TimeSpan OneDay = TimeSpan.FromHours(24);
        internal static readonly TimeSpan MaxShiftTime = TimeSpan.FromHours(48);

        private static readonly string[] SupportedDateFormats =
        {
            "yyyy-MM-dd",
            "yyyy/M/d",
            "yyyy/MM/dd",
            "yyyy-M-d"
        };

        public static bool TryParseShiftTime(string? text, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (text == null)
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            string[] parts = trimmed.Split(new[] { ':' }, StringSplitOptions.None);
            if (parts.Length < 2 || parts.Length > 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
            {
                return false;
            }

            int seconds = 0;
            if (parts.Length == 3 &&
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
            {
                return false;
            }

            if (hours < 0 || hours > 48 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59)
            {
                return false;
            }

            long totalTicks =
                (hours * TimeSpan.TicksPerHour) +
                (minutes * TimeSpan.TicksPerMinute) +
                (seconds * TimeSpan.TicksPerSecond);

            if (totalTicks > MaxShiftTime.Ticks)
            {
                return false;
            }

            result = new TimeSpan(totalTicks);
            return true;
        }

        public static TimeSpan ParseShiftTime(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (!TryParseShiftTime(text, out TimeSpan result))
            {
                throw new FormatException($"時刻表現 '{text}' の形式が不正です。'HH:mm' または 'HH:mm:ss'（00:00〜48:00）で指定してください。");
            }

            return result;
        }

        public static string FormatShiftTime(TimeSpan time)
        {
            long totalHours = time.Ticks / TimeSpan.TicksPerHour;
            if (time.Seconds == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}", totalHours, time.Minutes);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}", totalHours, time.Minutes, time.Seconds);
        }

        public static bool TryParseDate(string? text, out DateTime date)
        {
            date = default;
            if (text == null)
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            if (DateTime.TryParseExact(
                trimmed,
                SupportedDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
            {
                date = parsed.Date;
                return true;
            }

            return false;
        }

        public static DateTime ParseDate(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (!TryParseDate(text, out DateTime date))
            {
                throw new FormatException($"日付表現 '{text}' の形式が不正です。'yyyy-MM-dd' または 'yyyy/M/d' 形式で指定してください。");
            }

            return date;
        }

        public static string FormatDate(DateTime date)
        {
            return date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
