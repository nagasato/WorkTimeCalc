using System;
using System.Globalization;

namespace DateCalc.Holidays
{
    /// <summary>
    /// 祝日の種別を表します。
    /// </summary>
    public enum HolidayKind
    {
        /// <summary>
        /// 国民の祝日（固定祝日、ハッピーマンデー、春分・秋分等）。
        /// </summary>
        National = 0,

        /// <summary>
        /// 振替休日。
        /// </summary>
        Substitute = 1,

        /// <summary>
        /// 国民の休日（祝日に挟まれた平日）。
        /// </summary>
        CitizensHoliday = 2,

        /// <summary>
        /// 独自定義または外部データ由来のカスタム祝日。
        /// </summary>
        Custom = 3
    }

    /// <summary>
    /// 祝日に関する情報を表します。
    /// </summary>
    public sealed class HolidayInfo
    {
        /// <summary>
        /// <see cref="HolidayInfo"/> を初期化します。
        /// </summary>
        /// <param name="date">祝日の日付。</param>
        /// <param name="name">祝日名。</param>
        /// <param name="kind">祝日の種別。</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> が空白または空の場合にスローされます。</exception>
        public HolidayInfo(DateTime date, string name, HolidayKind kind = HolidayKind.National)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("祝日名は空にできません。", nameof(name));
            }

            Date = date.Date;
            Name = name.Trim();
            Kind = kind;
        }

        /// <summary>
        /// 祝日の日付（時刻成分 00:00:00）を取得します。
        /// </summary>
        public DateTime Date { get; }

#if NET6_0_OR_GREATER
        /// <summary>
        /// 祝日の日付を <see cref="System.DateOnly"/> として取得します。
        /// </summary>
        public DateOnly DateOnly => DateOnly.FromDateTime(Date);
#endif

        /// <summary>
        /// 祝日名を取得します。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 祝日の種別を取得します。
        /// </summary>
        public HolidayKind Kind { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} {1} ({2})", Date, Name, Kind);
        }
    }
}
