using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WorkTimeCalc.Holidays
{
    /// <summary>
    /// メモリ上に保持した任意の祝日セットを提供するイミュータブルな祝日プロバイダです。
    /// DB から読み込んだ祝日テーブルや独自定義の祝日の登録に適しています。
    /// </summary>
    public sealed class InMemoryHolidayProvider : IHolidayProvider
    {
        private readonly Dictionary<DateTime, HolidayInfo> _holidaysByDate;
        private readonly Dictionary<int, IReadOnlyList<HolidayInfo>> _holidaysByYear;

        /// <summary>
        /// <see cref="HolidayInfo"/> のコレクションから <see cref="InMemoryHolidayProvider"/> を初期化します。
        /// 同一日付が複数含まれる場合は後の要素で上書きされます。
        /// </summary>
        /// <param name="holidays">登録する祝日情報のコレクション。</param>
        /// <exception cref="ArgumentNullException"><paramref name="holidays"/> が <c>null</c> の場合にスローされます。</exception>
        public InMemoryHolidayProvider(IEnumerable<HolidayInfo> holidays)
        {
            if (holidays == null)
            {
                throw new ArgumentNullException(nameof(holidays));
            }

            _holidaysByDate = new Dictionary<DateTime, HolidayInfo>();
            foreach (HolidayInfo info in holidays)
            {
                if (info == null)
                {
                    throw new ArgumentException("祝日コレクションに null 要素を含めることはできません。", nameof(holidays));
                }

                _holidaysByDate[info.Date] = info;
            }

            _holidaysByYear = BuildYearIndex(_holidaysByDate);
        }

        /// <summary>
        /// 日付と祝日名の辞書から <see cref="InMemoryHolidayProvider"/> を初期化します。
        /// </summary>
        /// <param name="holidaysByDate">日付をキー、祝日名を値とする辞書。</param>
        /// <param name="kind">登録する祝日の種別（既定は <see cref="HolidayKind.Custom"/>）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="holidaysByDate"/> が <c>null</c> の場合にスローされます。</exception>
        public InMemoryHolidayProvider(IDictionary<DateTime, string> holidaysByDate, HolidayKind kind = HolidayKind.Custom)
        {
            if (holidaysByDate == null)
            {
                throw new ArgumentNullException(nameof(holidaysByDate));
            }

            _holidaysByDate = new Dictionary<DateTime, HolidayInfo>(holidaysByDate.Count);
            foreach (KeyValuePair<DateTime, string> entry in holidaysByDate)
            {
                _holidaysByDate[entry.Key.Date] = new HolidayInfo(entry.Key.Date, entry.Value, kind);
            }

            _holidaysByYear = BuildYearIndex(_holidaysByDate);
        }

        /// <inheritdoc />
        public bool TryGetHoliday(DateTime date, out HolidayInfo? holiday)
        {
            return _holidaysByDate.TryGetValue(date.Date, out holiday);
        }

        /// <inheritdoc />
        public IReadOnlyList<HolidayInfo> GetHolidays(int year)
        {
            return _holidaysByYear.TryGetValue(year, out IReadOnlyList<HolidayInfo>? list)
                ? list
                : Array.Empty<HolidayInfo>();
        }

        private static Dictionary<int, IReadOnlyList<HolidayInfo>> BuildYearIndex(Dictionary<DateTime, HolidayInfo> byDate)
        {
            var grouped = new Dictionary<int, List<HolidayInfo>>();
            foreach (HolidayInfo info in byDate.Values)
            {
                if (!grouped.TryGetValue(info.Date.Year, out List<HolidayInfo>? list))
                {
                    list = new List<HolidayInfo>();
                    grouped.Add(info.Date.Year, list);
                }

                list.Add(info);
            }

            var result = new Dictionary<int, IReadOnlyList<HolidayInfo>>(grouped.Count);
            foreach (KeyValuePair<int, List<HolidayInfo>> entry in grouped)
            {
                entry.Value.Sort((left, right) => left.Date.CompareTo(right.Date));
                result.Add(entry.Key, new ReadOnlyCollection<HolidayInfo>(entry.Value.ToArray()));
            }

            return result;
        }
    }
}
