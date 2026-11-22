using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DateCalc.Holidays;
using DateCalc.Serialization;

namespace DateCalc
{
    /// <summary>
    /// 複数の <see cref="IWorkCalendar"/>（工場別・部署別・シフト別など）を識別子で管理・提供する
    /// イミュータブルかつスレッドセーフなレジストリです。識別子の比較は大文字・小文字を区別しません（<see cref="StringComparer.OrdinalIgnoreCase"/>）。
    /// </summary>
    public sealed class WorkCalendarRegistry : IWorkCalendarProvider
    {
        private readonly Dictionary<string, IWorkCalendar> _calendars;
        private readonly IReadOnlyCollection<string> _calendarIds;

        /// <summary>
        /// 指定したカレンダーコレクションから <see cref="WorkCalendarRegistry"/> を初期化します。
        /// </summary>
        /// <param name="calendars">登録するカレンダー一覧。各カレンダーには空白以外の重複しない <see cref="IWorkCalendar.Id"/> が必要です。</param>
        /// <exception cref="ArgumentNullException"><paramref name="calendars"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">カレンダーに null 要素がある、Id が空白、または Id が重複している場合にスローされます。</exception>
        public WorkCalendarRegistry(IEnumerable<IWorkCalendar> calendars)
        {
            if (calendars == null)
            {
                throw new ArgumentNullException(nameof(calendars));
            }

            var map = new Dictionary<string, IWorkCalendar>(StringComparer.OrdinalIgnoreCase);
            var ids = new List<string>();

            foreach (IWorkCalendar calendar in calendars)
            {
                if (calendar == null)
                {
                    throw new ArgumentException("カレンダー一覧に null を含めることはできません。", nameof(calendars));
                }

                if (string.IsNullOrWhiteSpace(calendar.Id))
                {
                    throw new ArgumentException("レジストリに登録するすべてのカレンダーには Id が必要です。", nameof(calendars));
                }

                string id = calendar.Id!.Trim();
                if (map.ContainsKey(id))
                {
                    throw new ArgumentException($"カレンダー識別子 '{id}' が重複しています。", nameof(calendars));
                }

                map.Add(id, calendar);
                ids.Add(id);
            }

            _calendars = map;
            _calendarIds = new ReadOnlyCollection<string>(ids.ToArray());
        }

        /// <summary>
        /// 指定したカレンダー配列から <see cref="WorkCalendarRegistry"/> を初期化します。
        /// </summary>
        /// <param name="calendars">登録するカレンダー配列。</param>
        public WorkCalendarRegistry(params IWorkCalendar[] calendars)
            : this((IEnumerable<IWorkCalendar>)calendars)
        {
        }

        /// <summary>
        /// 複数の <see cref="WorkCalendarDefinition"/> から <see cref="WorkCalendarRegistry"/> を一括構築します。
        /// </summary>
        /// <param name="definitions">カレンダー定義のコレクション。</param>
        /// <param name="defaultHolidayProvider">各カレンダーの祝日プロバイダを外部指定（内閣府 CSV 等）に差し替える場合に指定します（省略可）。</param>
        /// <returns>構築された <see cref="WorkCalendarRegistry"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definitions"/> が <c>null</c> の場合にスローされます。</exception>
        public static WorkCalendarRegistry FromDefinitions(
            IEnumerable<WorkCalendarDefinition> definitions,
            IHolidayProvider? defaultHolidayProvider = null)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var calendars = new List<IWorkCalendar>();
            foreach (WorkCalendarDefinition def in definitions)
            {
                calendars.Add(WorkCalendar.FromDefinition(def, defaultHolidayProvider));
            }

            return new WorkCalendarRegistry(calendars);
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> CalendarIds => _calendarIds;

        /// <inheritdoc />
        public IWorkCalendar GetCalendar(string calendarId)
        {
            if (calendarId == null)
            {
                throw new ArgumentNullException(nameof(calendarId));
            }

            if (!_calendars.TryGetValue(calendarId, out IWorkCalendar? calendar))
            {
                throw new KeyNotFoundException($"識別子 '{calendarId}' の稼働カレンダーは登録されていません。");
            }

            return calendar;
        }

        /// <inheritdoc />
        public bool TryGetCalendar(string calendarId, out IWorkCalendar? calendar)
        {
            if (calendarId == null)
            {
                calendar = null;
                return false;
            }

            return _calendars.TryGetValue(calendarId, out calendar);
        }
    }
}
