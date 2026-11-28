using System;
using System.Collections.Generic;
using WorkTimeCalc.Holidays;
using WorkTimeCalc.Models;
using WorkTimeCalc.Serialization;

namespace WorkTimeCalc
{
    /// <summary>
    /// 稼働日判定・稼働日計算・実稼働時間算出を提供する稼働カレンダーのインターフェースです。
    /// </summary>
    public interface IWorkCalendar
    {
        /// <summary>
        /// カレンダー識別子を取得します（未設定の場合は <c>null</c>）。
        /// </summary>
        string? Id { get; }

        /// <summary>
        /// カレンダーの表示名を取得します（未設定の場合は <c>null</c>）。
        /// </summary>
        string? Name { get; }

        /// <summary>
        /// 1 稼働日あたりの稼働時間帯・休憩スケジュールを取得します。
        /// </summary>
        DailyWorkSchedule Schedule { get; }

        /// <summary>
        /// 定休曜日のコレクションを取得します。
        /// </summary>
        IReadOnlyCollection<DayOfWeek> NonWorkingDaysOfWeek { get; }

        /// <summary>
        /// 祝日判定に使用している <see cref="IHolidayProvider"/> を取得します。
        /// </summary>
        IHolidayProvider HolidayProvider { get; }

        /// <summary>
        /// 現在のカレンダー構成をシリアライズ可能な <see cref="WorkCalendarDefinition"/> DTO へ変換します。
        /// </summary>
        /// <returns>カレンダー定義 DTO。</returns>
        WorkCalendarDefinition ToDefinition();

        /// <summary>
        /// 指定した日付が稼働日かどうかを判定します。
        /// </summary>
        /// <param name="date">判定対象の日付（時刻成分は無視されます）。</param>
        /// <returns>稼働日の場合は <c>true</c>、非稼働日の場合は <c>false</c>。</returns>
        bool IsWorkingDay(DateTime date);

        /// <summary>
        /// 指定した日付の稼働日判定結果と判定理由の詳細を取得します。
        /// </summary>
        /// <param name="date">判定対象の日付（時刻成分は無視されます）。</param>
        /// <returns>判定結果と理由を含む <see cref="DayStatus"/>。</returns>
        DayStatus GetDayStatus(DateTime date);

        /// <summary>
        /// 指定した日付から指定された稼働日数だけ前後に移動した日付を算出します。
        /// </summary>
        /// <param name="date">基準日。</param>
        /// <param name="workingDays">加算する稼働日数（正で未来方向、負で過去方向、0 で基準日の日付）。</param>
        /// <returns>算出された稼働日（時刻成分 00:00:00）。</returns>
        DateTime AddWorkingDays(DateTime date, int workingDays);

        /// <summary>
        /// 指定した日付以降の最も近い稼働日を取得します。
        /// </summary>
        /// <param name="date">基準日。</param>
        /// <param name="includeSelf"><paramref name="date"/> 自身が稼働日の場合にそのまま返すかどうか（既定: <c>true</c>）。</param>
        /// <returns>見つかった稼働日（時刻成分 00:00:00）。</returns>
        DateTime GetNextWorkingDay(DateTime date, bool includeSelf = true);

        /// <summary>
        /// 指定した日付以前の最も近い稼働日を取得します。
        /// </summary>
        /// <param name="date">基準日。</param>
        /// <param name="includeSelf"><paramref name="date"/> 自身が稼働日の場合にそのまま返すかどうか（既定: <c>true</c>）。</param>
        /// <returns>見つかった稼働日（時刻成分 00:00:00）。</returns>
        DateTime GetPreviousWorkingDay(DateTime date, bool includeSelf = true);

        /// <summary>
        /// 指定期間 [startDate, endDate]（両端を含む閉区間）の稼働日数を数えます。
        /// </summary>
        /// <param name="startDate">開始日（含む）。</param>
        /// <param name="endDate">終了日（含む）。</param>
        /// <returns>期間内の稼働日数。</returns>
        int CountWorkingDays(DateTime startDate, DateTime endDate);

        /// <summary>
        /// 指定期間 [startDate, endDate]（両端を含む閉区間）のすべての稼働日を昇順で列挙します。
        /// </summary>
        /// <param name="startDate">開始日（含む）。</param>
        /// <param name="endDate">終了日（含む）。</param>
        /// <returns>稼働日の列挙。</returns>
        IEnumerable<DateTime> GetWorkingDays(DateTime startDate, DateTime endDate);

#if NET6_0_OR_GREATER
        /// <summary>
        /// 指定した <see cref="DateOnly"/> が稼働日かどうかを判定します。
        /// </summary>
        bool IsWorkingDay(DateOnly date);

        /// <summary>
        /// 指定した <see cref="DateOnly"/> の稼働日判定結果と判定理由の詳細を取得します。
        /// </summary>
        DayStatus GetDayStatus(DateOnly date);

        /// <summary>
        /// 指定した <see cref="DateOnly"/> から指定稼働日数だけ前後に移動した日付を算出します。
        /// </summary>
        DateOnly AddWorkingDays(DateOnly date, int workingDays);

        /// <summary>
        /// 指定した <see cref="DateOnly"/> 以降の最も近い稼働日を取得します。
        /// </summary>
        DateOnly GetNextWorkingDay(DateOnly date, bool includeSelf = true);

        /// <summary>
        /// 指定した <see cref="DateOnly"/> 以前の最も近い稼働日を取得します。
        /// </summary>
        DateOnly GetPreviousWorkingDay(DateOnly date, bool includeSelf = true);

        /// <summary>
        /// 指定期間 [startDate, endDate]（両端を含む閉区間）の稼働日数を数えます。
        /// </summary>
        int CountWorkingDays(DateOnly startDate, DateOnly endDate);

        /// <summary>
        /// 指定期間 [startDate, endDate]（両端を含む閉区間）のすべての稼働日を昇順で列挙します。
        /// </summary>
        IEnumerable<DateOnly> GetWorkingDays(DateOnly startDate, DateOnly endDate);

        /// <summary>
        /// 指定した稼働日（<see cref="DateOnly"/>）を始業日とする実効稼働時間帯の一覧を取得します。
        /// </summary>
        IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateOnly workingDate);
#endif

        /// <summary>
        /// 指定した日時が実効稼働時間帯内にあるかどうかを判定します（半開区間 [Start, End)）。
        /// 日跨ぎシフトの前日からの繰り越し稼働時間も含めて判定します。
        /// </summary>
        /// <param name="dateTime">判定対象の日時。</param>
        /// <returns>実効稼働時間内の場合は <c>true</c>。</returns>
        bool IsWorkingTime(DateTime dateTime);

        /// <summary>
        /// 指定した開始日時から指定した実稼働時間だけ進めた（負数の場合は戻した）完了日時を算出します。
        /// </summary>
        /// <param name="startDateTime">開始日時。</param>
        /// <param name="duration">加算する実稼働時間（負値で逆算、0 で startDateTime を返却）。</param>
        /// <returns>算出された完了日時。</returns>
        DateTime AddWorkingTime(DateTime startDateTime, TimeSpan duration);

        /// <summary>
        /// 指定した終了日時から指定した実稼働時間だけ遡った開始日時を算出します。
        /// </summary>
        /// <param name="endDateTime">終了日時。</param>
        /// <param name="duration">減算する実稼働時間（負値で未来方向へ加算、0 で endDateTime を返却）。</param>
        /// <returns>算出された開始日時。</returns>
        DateTime SubtractWorkingTime(DateTime endDateTime, TimeSpan duration);

        /// <summary>
        /// 2 つの日時 [startDateTime, endDateTime) の間に含まれる実稼働時間の合計を算出します。
        /// <paramref name="endDateTime"/> が <paramref name="startDateTime"/> より前の場合は負の <see cref="TimeSpan"/> を返します。
        /// </summary>
        /// <param name="startDateTime">開始日時。</param>
        /// <param name="endDateTime">終了日時。</param>
        /// <returns>実稼働時間の合計。</returns>
        TimeSpan GetWorkingTime(DateTime startDateTime, DateTime endDateTime);

        /// <summary>
        /// 指定した日時以降で最も近い稼働開始（既に稼働中の場合はそのまま）の日時を取得します。
        /// </summary>
        /// <param name="dateTime">基準日時。</param>
        /// <returns>稼働開始日時または <paramref name="dateTime"/>。</returns>
        DateTime GetNextWorkingTime(DateTime dateTime);

        /// <summary>
        /// 指定した日時以前で最も近い稼働時刻（既に稼働中の場合はそのまま、休憩中・休日の場合は直前の稼働終端）を取得します。
        /// </summary>
        /// <param name="dateTime">基準日時。</param>
        /// <returns>直近の稼働日時または <paramref name="dateTime"/>。</returns>
        DateTime GetPreviousWorkingTime(DateTime dateTime);

        /// <summary>
        /// 指定した日時範囲 [startDateTime, endDateTime) に含まれる実効稼働区間の一覧を取得します。
        /// 日付境界で連続する区間は結合されます。
        /// </summary>
        /// <param name="startDateTime">開始日時（含む）。</param>
        /// <param name="endDateTime">終了日時（含まない）。</param>
        /// <returns>実効稼働区間の一覧。</returns>
        IReadOnlyList<DateTimeInterval> GetWorkingIntervals(DateTime startDateTime, DateTime endDateTime);

        /// <summary>
        /// 指定した稼働日を始業日とする実効稼働時間帯の一覧を取得します。
        /// 非稼働日の場合は空のリストを返します。
        /// </summary>
        /// <param name="workingDate">始業日。</param>
        /// <returns>その始業日に属する実効稼働区間一覧（日跨ぎ分も含む）。</returns>
        IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateTime workingDate);

        /// <summary>
        /// 指定した日時がカレンダーに事前定義された実効残業時間帯内にあるかどうかを判定します（半開区間 [Start, End)）。
        /// </summary>
        /// <param name="dateTime">判定対象の日時。</param>
        /// <returns>事前定義の残業時間帯内の場合は <c>true</c>。</returns>
        bool IsOvertime(DateTime dateTime);

        /// <summary>
        /// 指定した残業ポリシーのもとで、指定日時が実効稼働時間帯内にあるかどうかを判定します（半開区間 [Start, End)）。
        /// </summary>
        /// <param name="dateTime">判定対象の日時。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>実効稼働時間内の場合は <c>true</c>。</returns>
        bool IsWorkingTime(DateTime dateTime, OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、開始日時から指定した実稼働時間だけ進めた（負数の場合は戻した）完了日時を算出します。
        /// </summary>
        /// <param name="startDateTime">開始日時。</param>
        /// <param name="duration">加算する実稼働時間（負値で逆算、0 で startDateTime を返却）。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>算出された完了日時。</returns>
        /// <exception cref="InvalidOperationException"><see cref="OvertimePolicyKind.MaxPerDay"/> 指定時に休憩で終日が覆われ残業上限分の時間を確保できない場合、または稼働日が存在しない場合にスローされます。</exception>
        DateTime AddWorkingTime(DateTime startDateTime, TimeSpan duration, OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、終了日時から指定した実稼働時間だけ遡った開始日時を算出します。
        /// </summary>
        /// <param name="endDateTime">終了日時。</param>
        /// <param name="duration">減算する実稼働時間（負値で未来方向へ加算、0 で endDateTime を返却）。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>算出された開始日時。</returns>
        /// <exception cref="InvalidOperationException"><see cref="OvertimePolicy.Unlimited"/> 指定時に条件を満たす有効な始業開始日時（稼働日の始業時刻以降）が存在しない場合や、稼働日が存在しない場合にスローされます。</exception>
        DateTime SubtractWorkingTime(DateTime endDateTime, TimeSpan duration, OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、2 つの日時 [startDateTime, endDateTime) の間に含まれる実稼働時間の合計を算出します。
        /// <paramref name="endDateTime"/> が <paramref name="startDateTime"/> より前の場合は負の <see cref="TimeSpan"/> を返します。
        /// </summary>
        /// <param name="startDateTime">開始日時。</param>
        /// <param name="endDateTime">終了日時。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>実稼働時間の合計。</returns>
        TimeSpan GetWorkingTime(DateTime startDateTime, DateTime endDateTime, OvertimePolicy overtimePolicy);

        /// <summary>
        /// カレンダー事前定義の残業ポリシー（<see cref="OvertimePolicy.CalendarDefined"/>）を用いて、
        /// 2 つの日時 [startDateTime, endDateTime) の実稼働時間を定時・残業・合計に分解して取得します。
        /// </summary>
        /// <param name="startDateTime">開始日時。</param>
        /// <param name="endDateTime">終了日時。</param>
        /// <returns>定時・残業・合計を含む <see cref="WorkingTimeBreakdown"/>。</returns>
        WorkingTimeBreakdown GetWorkingTimeBreakdown(DateTime startDateTime, DateTime endDateTime);

        /// <summary>
        /// 指定した残業ポリシーのもとで、2 つの日時 [startDateTime, endDateTime) の実稼働時間を定時・残業・合計に分解して取得します。
        /// </summary>
        /// <param name="startDateTime">開始日時。</param>
        /// <param name="endDateTime">終了日時。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>定時・残業・合計を含む <see cref="WorkingTimeBreakdown"/>。</returns>
        WorkingTimeBreakdown GetWorkingTimeBreakdown(
            DateTime startDateTime,
            DateTime endDateTime,
            OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、指定日時以降で最も近い稼働開始（既に稼働中の場合はそのまま）の日時を取得します。
        /// </summary>
        /// <param name="dateTime">基準日時。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>稼働開始日時または <paramref name="dateTime"/>。</returns>
        DateTime GetNextWorkingTime(DateTime dateTime, OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、指定日時以前で最も近い稼働時刻（既に稼働中の場合はそのまま、休憩中・休日の場合は直前の稼働終端）を取得します。
        /// </summary>
        /// <param name="dateTime">基準日時。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>直近の稼働日時または <paramref name="dateTime"/>。</returns>
        DateTime GetPreviousWorkingTime(DateTime dateTime, OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、指定日時範囲 [startDateTime, endDateTime) に含まれる実効稼働区間の一覧を取得します。
        /// 日付境界で連続する区間は結合されます。
        /// </summary>
        /// <param name="startDateTime">開始日時（含む）。</param>
        /// <param name="endDateTime">終了日時（含まない）。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>実効稼働区間の一覧。</returns>
        IReadOnlyList<DateTimeInterval> GetWorkingIntervals(
            DateTime startDateTime,
            DateTime endDateTime,
            OvertimePolicy overtimePolicy);

        /// <summary>
        /// 指定した残業ポリシーのもとで、指定稼働日を始業日とする実効稼働時間帯の一覧を取得します。
        /// 非稼働日の場合は空のリストを返します。<see cref="OvertimePolicy.Unlimited"/> を指定した場合は <see cref="ArgumentException"/> がスローされます。
        /// </summary>
        /// <param name="workingDate">始業日。</param>
        /// <param name="overtimePolicy">残業の扱い方を指定するポリシー。</param>
        /// <returns>その始業日に属する実効稼働区間一覧（日跨ぎ分も含む）。</returns>
        IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateTime workingDate, OvertimePolicy overtimePolicy);

#if NET6_0_OR_GREATER
        /// <summary>
        /// 指定した残業ポリシーのもとで、指定稼働日（<see cref="DateOnly"/>）を始業日とする実効稼働時間帯の一覧を取得します。
        /// </summary>
        IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateOnly workingDate, OvertimePolicy overtimePolicy);
#endif
    }
}
