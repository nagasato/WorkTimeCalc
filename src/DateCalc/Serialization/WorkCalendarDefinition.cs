using System;
using System.Collections.Generic;

namespace DateCalc.Serialization
{
    /// <summary>
    /// JSON 設定ファイルやデータベースレコードとの相互変換に使用するカレンダー定義 DTO です。
    /// </summary>
    public sealed class WorkCalendarDefinition
    {
        /// <summary>
        /// カレンダー識別子（複数カレンダー管理時のキー）を取得または設定します。
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// カレンダーの表示名を取得または設定します。
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 1 稼働日あたりの稼働時間帯一覧を取得または設定します。
        /// <c>null</c>（かつ <see cref="Breaks"/> も <c>null</c>）の場合は標準日勤（08:00-17:00、休憩 12:00-13:00）が使用されます。
        /// </summary>
        public List<TimeRangeDefinition>? WorkingPeriods { get; set; }

        /// <summary>
        /// 1 稼働日あたりの休憩時間帯一覧を取得または設定します。
        /// </summary>
        public List<TimeRangeDefinition>? Breaks { get; set; }

        /// <summary>
        /// 定休曜日のリストを取得または設定します。
        /// <c>null</c> の場合は土曜日・日曜日が既定値として使用されます。
        /// </summary>
        public List<DayOfWeek>? NonWorkingDaysOfWeek { get; set; }

        /// <summary>
        /// 内蔵の日本の国民の祝日判定エンジンを使用するかどうかを取得または設定します（既定値: <c>true</c>）。
        /// </summary>
        public bool UseJapanHolidays { get; set; } = true;

        /// <summary>
        /// 毎年固定の会社休日一覧を取得または設定します。
        /// </summary>
        public List<RecurringHolidayDefinition>? RecurringNonWorkingDays { get; set; }

        /// <summary>
        /// 個別日付の稼働日／非稼働日上書き一覧を取得または設定します。
        /// </summary>
        public List<DateOverrideDefinition>? DateOverrides { get; set; }

        /// <summary>
        /// 独自の祝日一覧を取得または設定します。
        /// </summary>
        public List<CustomHolidayDefinition>? CustomHolidays { get; set; }
    }

    /// <summary>
    /// 稼働時間帯・休憩時間帯の定義 DTO です。
    /// </summary>
    public sealed class TimeRangeDefinition
    {
        /// <summary>
        /// 開始時刻文字列（例: "08:00", "22:00"）を取得または設定します。
        /// </summary>
        public string Start { get; set; } = string.Empty;

        /// <summary>
        /// 終了時刻文字列（例: "17:00", "06:00", "30:00"）を取得または設定します。
        /// </summary>
        public string End { get; set; } = string.Empty;
    }

    /// <summary>
    /// 毎年固定の会社休日の定義 DTO です。
    /// </summary>
    public sealed class RecurringHolidayDefinition
    {
        /// <summary>
        /// 月（1〜12）を取得または設定します。
        /// </summary>
        public int Month { get; set; }

        /// <summary>
        /// 日（1〜31）を取得または設定します。
        /// </summary>
        public int Day { get; set; }

        /// <summary>
        /// 休日理由・名称（例: "年末年始"）を取得または設定します。
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 個別日付に対する稼働日／非稼働日上書きの定義 DTO です。
    /// </summary>
    public sealed class DateOverrideDefinition
    {
        /// <summary>
        /// 対象日付文字列（例: "2026-05-04"）を取得または設定します。
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// 稼働日にする場合は <c>true</c>、非稼働日にする場合は <c>false</c> を取得または設定します。
        /// </summary>
        public bool IsWorkingDay { get; set; }

        /// <summary>
        /// 特例理由のメモを取得または設定します。
        /// </summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 独自祝日の定義 DTO です。
    /// </summary>
    public sealed class CustomHolidayDefinition
    {
        /// <summary>
        /// 祝日の日付文字列（例: "2026-08-15"）を取得または設定します。
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// 祝日名を取得または設定します。
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
}
