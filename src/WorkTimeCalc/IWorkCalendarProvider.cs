using System.Collections.Generic;

namespace WorkTimeCalc
{
    /// <summary>
    /// 識別子を指定して複数の <see cref="IWorkCalendar"/> を提供するプロバイダのインターフェースです（DI 用）。
    /// </summary>
    public interface IWorkCalendarProvider
    {
        /// <summary>
        /// 登録されているすべてのカレンダー識別子を取得します。
        /// </summary>
        IReadOnlyCollection<string> CalendarIds { get; }

        /// <summary>
        /// 指定した識別子に対応するカレンダーを取得します。
        /// </summary>
        /// <param name="calendarId">カレンダー識別子。</param>
        /// <returns>対応する <see cref="IWorkCalendar"/>。</returns>
        /// <exception cref="KeyNotFoundException">指定された識別子のカレンダーが見つからない場合にスローされます。</exception>
        IWorkCalendar GetCalendar(string calendarId);

        /// <summary>
        /// 指定した識別子に対応するカレンダーの取得を試みます。
        /// </summary>
        /// <param name="calendarId">カレンダー識別子。</param>
        /// <param name="calendar">見つかった場合はそのカレンダー、見つからない場合は <c>null</c>。</param>
        /// <returns>見つかった場合は <c>true</c>。</returns>
        bool TryGetCalendar(string calendarId, out IWorkCalendar? calendar);
    }
}
