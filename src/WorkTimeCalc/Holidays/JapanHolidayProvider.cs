using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WorkTimeCalc.Holidays
{
    /// <summary>
    /// 「国民の祝日に関する法律」に基づき、日本の祝日を算出するプロバイダです。
    /// 年単位の計算結果をスレッドセーフにキャッシュします。
    /// </summary>
    public sealed class JapanHolidayProvider : IHolidayProvider
    {
        private static readonly DateTime SubstituteStartDate = new DateTime(1973, 4, 12);

        private readonly ConcurrentDictionary<int, YearHolidays> _cache =
            new ConcurrentDictionary<int, YearHolidays>();

        /// <summary>
        /// 共有の既定インスタンスを取得します。
        /// </summary>
        public static JapanHolidayProvider Instance { get; } = new JapanHolidayProvider();

        /// <summary>
        /// <see cref="JapanHolidayProvider"/> の新しいインスタンスを初期化します。
        /// </summary>
        public JapanHolidayProvider()
        {
        }

        /// <inheritdoc />
        public bool TryGetHoliday(DateTime date, out HolidayInfo? holiday)
        {
            DateTime targetDate = date.Date;
            YearHolidays yearData = _cache.GetOrAdd(targetDate.Year, CalculateYearHolidays);
            return yearData.ByDate.TryGetValue(targetDate, out holiday);
        }

        /// <inheritdoc />
        public IReadOnlyList<HolidayInfo> GetHolidays(int year)
        {
            if (year < 1 || year > 9999)
            {
                throw new ArgumentOutOfRangeException(nameof(year), year, "西暦年は 1 から 9999 の間で指定してください。");
            }

            YearHolidays yearData = _cache.GetOrAdd(year, CalculateYearHolidays);
            return yearData.OrderedList;
        }

        private static YearHolidays CalculateYearHolidays(int year)
        {
            if (year < 1948)
            {
                return YearHolidays.Empty;
            }

            var baseHolidays = new Dictionary<DateTime, HolidayInfo>();

            void AddBase(int month, int day, string name)
            {
                var date = new DateTime(year, month, day);
                if (!baseHolidays.ContainsKey(date))
                {
                    baseHolidays.Add(date, new HolidayInfo(date, name, HolidayKind.National));
                }
            }

            // 1月
            if (year >= 1949)
            {
                AddBase(1, 1, "元日");
            }

            if (year >= 2000)
            {
                DateTime seijin = GetNthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 2);
                AddBase(seijin.Month, seijin.Day, "成人の日");
            }
            else if (year >= 1949)
            {
                AddBase(1, 15, "成人の日");
            }

            // 2月
            if (year >= 1967)
            {
                AddBase(2, 11, "建国記念の日");
            }

            if (year >= 2020)
            {
                AddBase(2, 23, "天皇誕生日");
            }

            if (year == 1989)
            {
                AddBase(2, 24, "昭和天皇の大喪の礼");
            }

            // 3月: 春分の日
            if (year >= 1949)
            {
                int vernalDay = CalculateVernalEquinoxDay(year);
                AddBase(3, vernalDay, "春分の日");
            }

            // 4月
            if (year == 1959)
            {
                AddBase(4, 10, "皇太子明仁親王の結婚の儀");
            }

            if (year >= 2007)
            {
                AddBase(4, 29, "昭和の日");
            }
            else if (year >= 1989)
            {
                AddBase(4, 29, "みどりの日");
            }
            else if (year >= 1949)
            {
                AddBase(4, 29, "天皇誕生日");
            }

            // 5月
            if (year == 2019)
            {
                AddBase(5, 1, "天皇の即位の日");
            }

            if (year >= 1949)
            {
                AddBase(5, 3, "憲法記念日");
            }

            if (year >= 2007)
            {
                AddBase(5, 4, "みどりの日");
            }

            if (year >= 1949)
            {
                AddBase(5, 5, "こどもの日");
            }

            // 6月
            if (year == 1993)
            {
                AddBase(6, 9, "皇太子徳仁親王の結婚の儀");
            }

            // 7月（2020年・2021年は東京五輪特措法により移動）
            if (year == 2020)
            {
                AddBase(7, 23, "海の日");
                AddBase(7, 24, "スポーツの日");
            }
            else if (year == 2021)
            {
                AddBase(7, 22, "海の日");
                AddBase(7, 23, "スポーツの日");
            }
            else if (year >= 2003)
            {
                DateTime umi = GetNthWeekdayOfMonth(year, 7, DayOfWeek.Monday, 3);
                AddBase(umi.Month, umi.Day, "海の日");
            }
            else if (year >= 1996)
            {
                AddBase(7, 20, "海の日");
            }

            // 8月（2020年・2021年は東京五輪特措法により移動）
            if (year == 2020)
            {
                AddBase(8, 10, "山の日");
            }
            else if (year == 2021)
            {
                AddBase(8, 8, "山の日");
            }
            else if (year >= 2016)
            {
                AddBase(8, 11, "山の日");
            }

            // 9月
            if (year >= 2003)
            {
                DateTime keiro = GetNthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 3);
                AddBase(keiro.Month, keiro.Day, "敬老の日");
            }
            else if (year >= 1966)
            {
                AddBase(9, 15, "敬老の日");
            }

            if (year >= 1948)
            {
                int autumnalDay = CalculateAutumnalEquinoxDay(year);
                AddBase(9, autumnalDay, "秋分の日");
            }

            // 10月
            if (year >= 2022)
            {
                DateTime sports = GetNthWeekdayOfMonth(year, 10, DayOfWeek.Monday, 2);
                AddBase(sports.Month, sports.Day, "スポーツの日");
            }
            else if (year >= 2000 && year <= 2019)
            {
                DateTime taiiku = GetNthWeekdayOfMonth(year, 10, DayOfWeek.Monday, 2);
                AddBase(taiiku.Month, taiiku.Day, "体育の日");
            }
            else if (year >= 1966 && year <= 1999)
            {
                AddBase(10, 10, "体育の日");
            }

            if (year == 2019)
            {
                AddBase(10, 22, "即位礼正殿の儀");
            }

            // 11月
            if (year >= 1948)
            {
                AddBase(11, 3, "文化の日");
                AddBase(11, 23, "勤労感謝の日");
            }

            if (year == 1990)
            {
                AddBase(11, 12, "即位礼正殿の儀");
            }

            // 12月（平成天皇誕生日: 1989〜2018、2019年は天皇誕生日なし）
            if (year >= 1989 && year <= 2018)
            {
                AddBase(12, 23, "天皇誕生日");
            }

            // 全祝日マップへ反映
            var allHolidays = new Dictionary<DateTime, HolidayInfo>(baseHolidays);

            // 振替休日（1973年4月12日以降）
            var sortedBaseDates = new List<DateTime>(baseHolidays.Keys);
            sortedBaseDates.Sort();

            foreach (DateTime baseDate in sortedBaseDates)
            {
                if (baseDate < SubstituteStartDate || baseDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    continue;
                }

                if (year >= 2007)
                {
                    DateTime candidate = baseDate.AddDays(1);
                    while (baseHolidays.ContainsKey(candidate))
                    {
                        candidate = candidate.AddDays(1);
                    }

                    if (!allHolidays.ContainsKey(candidate))
                    {
                        allHolidays.Add(candidate, new HolidayInfo(candidate, "振替休日", HolidayKind.Substitute));
                    }
                }
                else
                {
                    DateTime nextDay = baseDate.AddDays(1);
                    if (!baseHolidays.ContainsKey(nextDay) && !allHolidays.ContainsKey(nextDay))
                    {
                        allHolidays.Add(nextDay, new HolidayInfo(nextDay, "振替休日", HolidayKind.Substitute));
                    }
                }
            }

            // 国民の休日（1986年以降、前後がともに「国民の祝日」に挟まれた非祝日の平日）
            if (year >= 1986)
            {
                foreach (DateTime baseDate in sortedBaseDates)
                {
                    if (baseDate.Month == 12 && baseDate.Day >= 30)
                    {
                        continue;
                    }

                    DateTime twoDaysLater = baseDate.AddDays(2);
                    if (twoDaysLater.Year != year)
                    {
                        continue;
                    }

                    if (baseHolidays.ContainsKey(twoDaysLater))
                    {
                        DateTime middle = baseDate.AddDays(1);
                        if (!allHolidays.ContainsKey(middle) && middle.DayOfWeek != DayOfWeek.Sunday)
                        {
                            allHolidays.Add(middle, new HolidayInfo(middle, "国民の休日", HolidayKind.CitizensHoliday));
                        }
                    }
                }
            }

            var ordered = new List<HolidayInfo>(allHolidays.Values);
            ordered.Sort((left, right) => left.Date.CompareTo(right.Date));

            return new YearHolidays(
                new ReadOnlyDictionary<DateTime, HolidayInfo>(allHolidays),
                new ReadOnlyCollection<HolidayInfo>(ordered));
        }

        private static DateTime GetNthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int nth)
        {
            var first = new DateTime(year, month, 1);
            int offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
            int day = 1 + offset + ((nth - 1) * 7);
            return new DateTime(year, month, day);
        }

        private static int CalculateVernalEquinoxDay(int year)
        {
            // 国立天文台準拠の近似式（1949〜2150年対応。1948〜1979 年のうるう年補正は Math.Floor((year - 1980) / 4.0) で厳密整合）
            if (year <= 1979)
            {
                return (int)Math.Floor(20.8357 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4.0));
            }

            if (year <= 2099)
            {
                return (int)Math.Floor(20.8431 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4.0));
            }

            return (int)Math.Floor(21.8510 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4.0));
        }

        private static int CalculateAutumnalEquinoxDay(int year)
        {
            if (year <= 1979)
            {
                return (int)Math.Floor(23.2588 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4.0));
            }

            if (year <= 2099)
            {
                return (int)Math.Floor(23.2488 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4.0));
            }

            return (int)Math.Floor(24.2488 + (0.242194 * (year - 1980)) - Math.Floor((year - 1980) / 4.0));
        }

        private sealed class YearHolidays
        {
            public static YearHolidays Empty { get; } = new YearHolidays(
                new ReadOnlyDictionary<DateTime, HolidayInfo>(new Dictionary<DateTime, HolidayInfo>()),
                new ReadOnlyCollection<HolidayInfo>(Array.Empty<HolidayInfo>()));

            public YearHolidays(
                IReadOnlyDictionary<DateTime, HolidayInfo> byDate,
                IReadOnlyList<HolidayInfo> orderedList)
            {
                ByDate = byDate;
                OrderedList = orderedList;
            }

            public IReadOnlyDictionary<DateTime, HolidayInfo> ByDate { get; }

            public IReadOnlyList<HolidayInfo> OrderedList { get; }
        }
    }
}
