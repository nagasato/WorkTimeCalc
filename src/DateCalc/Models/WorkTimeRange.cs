using System;
using DateCalc.Internal;

namespace DateCalc.Models
{
    /// <summary>
    /// 始業日を基準とした稼働時間帯または休憩時間帯を表します（半開区間 [Start, End)）。
    /// 日跨ぎシフトに対応するため、End は最大 48:00（翌日の 24:00）まで表現できます。
    /// </summary>
    public readonly struct WorkTimeRange : IEquatable<WorkTimeRange>, IComparable<WorkTimeRange>
    {
        /// <summary>
        /// 指定した開始時刻と終了時刻から <see cref="WorkTimeRange"/> を初期化します。
        /// </summary>
        /// <param name="start">始業日 00:00 からの開始オフセット（00:00 以上、48:00 未満）。</param>
        /// <param name="end">始業日 00:00 からの終了オフセット（start より大きく、48:00 以下、かつ長さ 24 時間以下）。</param>
        /// <exception cref="ArgumentOutOfRangeException">時刻が有効範囲外の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">時間帯の長さが 24 時間を超える場合にスローされます。</exception>
        public WorkTimeRange(TimeSpan start, TimeSpan end)
        {
            if (start < TimeSpan.Zero || start >= TimeFormatHelper.MaxShiftTime)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, "開始時刻は 00:00 以上 48:00 未満である必要があります。");
            }

            if (end <= start || end > TimeFormatHelper.MaxShiftTime)
            {
                throw new ArgumentOutOfRangeException(nameof(end), end, "終了時刻は開始時刻より大きく、48:00 以下である必要があります。");
            }

            if (end - start > TimeFormatHelper.OneDay)
            {
                throw new ArgumentException("1 つの時間帯の長さは 24 時間以下である必要があります。", nameof(end));
            }

            Start = start;
            End = end;
        }

        /// <summary>
        /// 始業日 00:00 を基準とした開始時刻（含む）を取得します。
        /// </summary>
        public TimeSpan Start { get; }

        /// <summary>
        /// 始業日 00:00 を基準とした終了時刻（含まない）を取得します。
        /// </summary>
        public TimeSpan End { get; }

        /// <summary>
        /// この時間帯の長さを取得します。
        /// </summary>
        public TimeSpan Duration => End - Start;

        /// <summary>
        /// 終了時刻が翌日（24:00 を超える時刻）に跨ぐかどうかを取得します。
        /// </summary>
        public bool SpansMidnight => End > TimeFormatHelper.OneDay;

        /// <summary>
        /// 時刻指定（終了時刻が開始時刻より小さい場合は自動的に翌日扱い）から <see cref="WorkTimeRange"/> を生成します。
        /// </summary>
        /// <param name="start">開始時刻（00:00 以上、48:00 未満）。</param>
        /// <param name="end">終了時刻。start（24:00未満）より小さい場合は翌日（+24時間）として解釈されます。24:00 や 30:00 のような明示表記も受け付けます。</param>
        /// <returns>生成された <see cref="WorkTimeRange"/>。</returns>
        /// <exception cref="ArgumentOutOfRangeException">時刻が有効範囲外の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">開始と終了が同じ、24時間超開始時に終了が開始未満、または長さが 24 時間を超える場合にスローされます。</exception>
        public static WorkTimeRange FromClock(TimeSpan start, TimeSpan end)
        {
            if (start < TimeSpan.Zero || start >= TimeFormatHelper.MaxShiftTime)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, "開始時刻は 00:00 以上 48:00 未満である必要があります。");
            }

            if (end < TimeSpan.Zero || end > TimeFormatHelper.MaxShiftTime)
            {
                throw new ArgumentOutOfRangeException(nameof(end), end, "終了時刻は 00:00 以上 48:00 以下である必要があります。");
            }

            if (end == start)
            {
                throw new ArgumentException("開始時刻と終了時刻を同じにすることはできません。", nameof(end));
            }

            TimeSpan normalizedEnd;
            if (end < start)
            {
                if (start >= TimeFormatHelper.OneDay)
                {
                    throw new ArgumentException("開始時刻が 24:00 以上の場合は、終了時刻も開始時刻より大きい 24 時間超表記で指定してください。", nameof(end));
                }

                normalizedEnd = end + TimeFormatHelper.OneDay;
            }
            else
            {
                normalizedEnd = end;
            }

            return new WorkTimeRange(start, normalizedEnd);
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// <see cref="TimeOnly"/> の時刻指定（終了が開始より小さい場合は自動的に翌日扱い）から <see cref="WorkTimeRange"/> を生成します。
        /// </summary>
        /// <param name="start">開始時刻。</param>
        /// <param name="end">終了時刻。開始より前の時刻の場合は翌日として解釈されます。</param>
        /// <returns>生成された <see cref="WorkTimeRange"/>。</returns>
        public static WorkTimeRange FromClock(TimeOnly start, TimeOnly end)
        {
            return FromClock(start.ToTimeSpan(), end.ToTimeSpan());
        }
#endif

        /// <summary>
        /// 文字列（例: "08:00-17:00", "22:00-06:00", "22:00-30:00"）を解析して <see cref="WorkTimeRange"/> を生成します。
        /// </summary>
        /// <param name="text">時間帯を表す文字列。</param>
        /// <returns>解析された <see cref="WorkTimeRange"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="FormatException">形式が不正な場合にスローされます。</exception>
        public static WorkTimeRange Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (!TryParse(text, out WorkTimeRange range))
            {
                throw new FormatException($"時間帯表現 '{text}' の形式が不正です。'HH:mm-HH:mm' 形式で指定してください。");
            }

            return range;
        }

        /// <summary>
        /// 文字列を <see cref="WorkTimeRange"/> に変換を試みます。
        /// </summary>
        /// <param name="text">時間帯を表す文字列。</param>
        /// <param name="range">変換成功時の結果。</param>
        /// <returns>変換に成功した場合は <c>true</c>、失敗した場合は <c>false</c>。</returns>
        public static bool TryParse(string? text, out WorkTimeRange range)
        {
            range = default;
            if (text == null)
            {
                return false;
            }

            string[] parts = text.Split(new[] { '-' }, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!TimeFormatHelper.TryParseShiftTime(parts[0], out TimeSpan start) ||
                !TimeFormatHelper.TryParseShiftTime(parts[1], out TimeSpan end))
            {
                return false;
            }

            if (start >= TimeFormatHelper.MaxShiftTime || start == end)
            {
                return false;
            }

            TimeSpan normalizedEnd;
            if (end < start)
            {
                if (start >= TimeFormatHelper.OneDay)
                {
                    return false;
                }

                normalizedEnd = end + TimeFormatHelper.OneDay;
            }
            else
            {
                normalizedEnd = end;
            }

            if (normalizedEnd > TimeFormatHelper.MaxShiftTime ||
                normalizedEnd - start > TimeFormatHelper.OneDay)
            {
                return false;
            }

            range = new WorkTimeRange(start, normalizedEnd);
            return true;
        }

        /// <summary>
        /// 指定した日内オフセット時刻がこの時間帯に含まれるかを判定します（Start &lt;= time &lt; End）。
        /// </summary>
        /// <param name="offset">始業日 00:00 からの経過時間。</param>
        /// <returns>含まれる場合は <c>true</c>。</returns>
        public bool Contains(TimeSpan offset)
        {
            return offset >= Start && offset < End;
        }

        /// <inheritdoc />
        public bool Equals(WorkTimeRange other)
        {
            return Start.Equals(other.Start) && End.Equals(other.End);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is WorkTimeRange other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }

        /// <inheritdoc />
        public int CompareTo(WorkTimeRange other)
        {
            int startComparison = Start.CompareTo(other.Start);
            return startComparison != 0 ? startComparison : End.CompareTo(other.End);
        }

        /// <summary>
        /// 2 つの <see cref="WorkTimeRange"/> が等しいかどうかを判定します。
        /// </summary>
        public static bool operator ==(WorkTimeRange left, WorkTimeRange right) => left.Equals(right);

        /// <summary>
        /// 2 つの <see cref="WorkTimeRange"/> が等しくないかどうかを判定します。
        /// </summary>
        public static bool operator !=(WorkTimeRange left, WorkTimeRange right) => !left.Equals(right);

        /// <summary>
        /// 左辺が右辺より小さいかどうかを比較します。
        /// </summary>
        public static bool operator <(WorkTimeRange left, WorkTimeRange right) => left.CompareTo(right) < 0;

        /// <summary>
        /// 左辺が右辺以下かどうかを比較します。
        /// </summary>
        public static bool operator <=(WorkTimeRange left, WorkTimeRange right) => left.CompareTo(right) <= 0;

        /// <summary>
        /// 左辺が右辺より大きいかどうかを比較します。
        /// </summary>
        public static bool operator >(WorkTimeRange left, WorkTimeRange right) => left.CompareTo(right) > 0;

        /// <summary>
        /// 左辺が右辺以上かどうかを比較します。
        /// </summary>
        public static bool operator >=(WorkTimeRange left, WorkTimeRange right) => left.CompareTo(right) >= 0;

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{TimeFormatHelper.FormatShiftTime(Start)}-{TimeFormatHelper.FormatShiftTime(End)}";
        }
    }
}
