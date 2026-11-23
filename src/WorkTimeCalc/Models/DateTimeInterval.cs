using System;
using System.Globalization;

namespace WorkTimeCalc.Models
{
    /// <summary>
    /// 絶対日時の半開区間 [Start, End) を表します。
    /// </summary>
    public readonly struct DateTimeInterval : IEquatable<DateTimeInterval>
    {
        /// <summary>
        /// 指定した開始日時と終了日時で <see cref="DateTimeInterval"/> を初期化します。
        /// </summary>
        /// <param name="start">開始日時（含む）。</param>
        /// <param name="end">終了日時（含まない）。start より大きい必要があります。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="end"/> が <paramref name="start"/> 以下の場合にスローされます。</exception>
        public DateTimeInterval(DateTime start, DateTime end)
        {
            if (end <= start)
            {
                throw new ArgumentOutOfRangeException(nameof(end), end, "終了日時は開始日時より後である必要があります。");
            }

            Start = start;
            End = end;
        }

        /// <summary>
        /// 区間の開始日時（含む）を取得します。
        /// </summary>
        public DateTime Start { get; }

        /// <summary>
        /// 区間の終了日時（含まない）を取得します。
        /// </summary>
        public DateTime End { get; }

        /// <summary>
        /// この区間の長さを取得します。
        /// </summary>
        public TimeSpan Duration => End - Start;

        /// <summary>
        /// 指定した日時がこの区間に含まれるかどうかを判定します（Start &lt;= value &lt; End）。
        /// </summary>
        /// <param name="value">判定する日時。</param>
        /// <returns>含まれる場合は <c>true</c>。</returns>
        public bool Contains(DateTime value)
        {
            return value >= Start && value < End;
        }

        /// <inheritdoc />
        public bool Equals(DateTimeInterval other)
        {
            return Start.Equals(other.Start) && End.Equals(other.End);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is DateTimeInterval other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }

        /// <summary>
        /// 2 つの <see cref="DateTimeInterval"/> が等しいかどうかを判定します。
        /// </summary>
        public static bool operator ==(DateTimeInterval left, DateTimeInterval right) => left.Equals(right);

        /// <summary>
        /// 2 つの <see cref="DateTimeInterval"/> が等しくないかどうかを判定します。
        /// </summary>
        public static bool operator !=(DateTimeInterval left, DateTimeInterval right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0:yyyy-MM-dd HH:mm:ss}, {1:yyyy-MM-dd HH:mm:ss})",
                Start,
                End);
        }
    }
}
