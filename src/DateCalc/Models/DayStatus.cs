using System;
using System.Globalization;

namespace DateCalc.Models
{
    /// <summary>
    /// カレンダー上の日付が稼働日または非稼働日と判定された理由を表します。
    /// </summary>
    public enum DayStatusReason
    {
        /// <summary>
        /// 通常の稼働日（曜日・祝日・特例のいずれの休日にも該当しない）。
        /// </summary>
        RegularWorkingDay = 0,

        /// <summary>
        /// 定休曜日（既定では土曜日・日曜日）。
        /// </summary>
        Weekend = 1,

        /// <summary>
        /// 祝日プロバイダにより判定された祝日（国民の祝日・振替休日・国民の休日・独自祝日等）。
        /// </summary>
        PublicHoliday = 2,

        /// <summary>
        /// 毎年固定の会社休日（例: 年末年始 12/29〜1/3、創立記念日）。
        /// </summary>
        RecurringHoliday = 3,

        /// <summary>
        /// 個別日付の特例上書きによる稼働日（休日出勤・祝日稼働等）。
        /// </summary>
        OverrideWorkingDay = 4,

        /// <summary>
        /// 個別日付の特例上書きによる非稼働日（夏季休暇・臨時休業等）。
        /// </summary>
        OverrideNonWorkingDay = 5
    }

    /// <summary>
    /// 特定日付における稼働日判定の結果と詳細理由を表します。
    /// </summary>
    public sealed class DayStatus
    {
        /// <summary>
        /// <see cref="DayStatus"/> を初期化します。
        /// </summary>
        /// <param name="date">対象日付。</param>
        /// <param name="isWorkingDay">稼働日かどうか。</param>
        /// <param name="reason">判定理由の区分。</param>
        /// <param name="description">祝日名や特例理由などの補足説明（任意）。</param>
        public DayStatus(DateTime date, bool isWorkingDay, DayStatusReason reason, string? description = null)
        {
            Date = date.Date;
            IsWorkingDay = isWorkingDay;
            Reason = reason;
            Description = description;
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
        /// この日が稼働日であるかどうかを取得します。
        /// </summary>
        public bool IsWorkingDay { get; }

        /// <summary>
        /// 判定理由の区分を取得します。
        /// </summary>
        public DayStatusReason Reason { get; }

        /// <summary>
        /// 祝日名や会社休日の名称、特例理由などの説明を取得します。
        /// </summary>
        public string? Description { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            string state = IsWorkingDay ? "稼働日" : "非稼働日";
            string dateText = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Description)
                ? $"{dateText} {state} ({Reason})"
                : $"{dateText} {state} ({Reason}: {Description})";
        }
    }
}
