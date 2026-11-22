using System;
using System.Globalization;

namespace DateCalc.Models
{
    /// <summary>
    /// 年に依存しない月日の組み合わせ（毎年固定の休日指定等）を表します。
    /// </summary>
    public readonly struct MonthDay : IEquatable<MonthDay>, IComparable<MonthDay>
    {
        /// <summary>
        /// 指定した月と日で <see cref="MonthDay"/> を初期化します。
        /// </summary>
        /// <param name="month">月（1〜12）。</param>
        /// <param name="day">日（その月に存在する有効な日付、2 月はうるう年基準で 29 日まで許容）。</param>
        /// <exception cref="ArgumentOutOfRangeException">月または日が有効範囲外の場合にスローされます。</exception>
        public MonthDay(int month, int day)
        {
            if (month < 1 || month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month), month, "月は 1 から 12 の間で指定してください。");
            }

            int maxDays = DateTime.DaysInMonth(2000, month);
            if (day < 1 || day > maxDays)
            {
                throw new ArgumentOutOfRangeException(nameof(day), day, $"{month} 月の日付は 1 から {maxDays} の間で指定してください。");
            }

            Month = month;
            Day = day;
        }

        /// <summary>
        /// 月（1〜12）を取得します。
        /// </summary>
        public int Month { get; }

        /// <summary>
        /// 日（1〜31）を取得します。
        /// </summary>
        public int Day { get; }

        /// <summary>
        /// 指定した <see cref="DateTime"/> の月日と一致するかを判定します。
        /// </summary>
        /// <param name="date">判定する日付。</param>
        /// <returns>月日が一致する場合は <c>true</c>。</returns>
        public bool Matches(DateTime date)
        {
            return date.Month == Month && date.Day == Day;
        }

        /// <inheritdoc />
        public bool Equals(MonthDay other)
        {
            return Month == other.Month && Day == other.Day;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is MonthDay other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Month * 397) ^ Day;
            }
        }

        /// <inheritdoc />
        public int CompareTo(MonthDay other)
        {
            int monthCmp = Month.CompareTo(other.Month);
            return monthCmp != 0 ? monthCmp : Day.CompareTo(other.Day);
        }

        /// <summary>
        /// 2 つの <see cref="MonthDay"/> が等しいかどうかを判定します。
        /// </summary>
        public static bool operator ==(MonthDay left, MonthDay right) => left.Equals(right);

        /// <summary>
        /// 2 つの <see cref="MonthDay"/> が等しくないかどうかを判定します。
        /// </summary>
        public static bool operator !=(MonthDay left, MonthDay right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}/{1:D2}", Month, Day);
        }
    }
}
