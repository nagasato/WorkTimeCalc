using System;
using System.Globalization;

namespace WorkTimeCalc.Models
{
    /// <summary>
    /// 特定の日付に対する稼働日／非稼働日の特例上書きを表します。
    /// </summary>
    public sealed class DateOverride
    {
        /// <summary>
        /// <see cref="DateOverride"/> を初期化します。
        /// </summary>
        /// <param name="date">対象日付。</param>
        /// <param name="isWorkingDay">稼働日にする場合は <c>true</c>、非稼働日にする場合は <c>false</c>。</param>
        /// <param name="reason">上書き理由のメモ（任意）。</param>
        public DateOverride(DateTime date, bool isWorkingDay, string? reason = null)
        {
            Date = date.Date;
            IsWorkingDay = isWorkingDay;
            Reason = reason;
        }

        /// <summary>
        /// 対象日付（時刻成分 00:00:00）を取得します。
        /// </summary>
        public DateTime Date { get; }

#if NET6_0_OR_GREATER
        /// <summary>
        /// 対象日付を <see cref="System.DateOnly"/> として取得します。
        /// </summary>
        public DateOnly DateOnly => DateOnly.FromDateTime(Date);
#endif

        /// <summary>
        /// 上書き後の稼働日フラグ（<c>true</c>: 稼働日、<c>false</c>: 非稼働日）を取得します。
        /// </summary>
        public bool IsWorkingDay { get; }

        /// <summary>
        /// 特例上書きの理由を取得します。
        /// </summary>
        public string? Reason { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            string kind = IsWorkingDay ? "稼働日" : "非稼働日";
            string dateText = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Reason) ? $"{dateText} -> {kind}" : $"{dateText} -> {kind} ({Reason})";
        }
    }
}
