using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DateCalc.Holidays;
using DateCalc.Internal;
using DateCalc.Models;
using DateCalc.Serialization;

namespace DateCalc
{
    /// <summary>
    /// 稼働日判定・稼働日計算・実稼働時間計算を行うイミュータブルかつスレッドセーフな稼働カレンダーです。
    /// 日勤・複数休憩・日跨ぎ稼働（夜勤）・休日出勤上書き・完全オフライン祝日判定をサポートします。
    /// </summary>
    public sealed class WorkCalendar : IWorkCalendar
    {
        private const int MaxConsecutiveNonWorkingDays = 3660;
        private static readonly DateTime MinSearchDate = DateTime.MinValue.Date;
        private static readonly DateTime MaxSearchDate = DateTime.MaxValue.Date;

        private readonly bool[] _nonWorkingDayFlags;
        private readonly bool _allSevenDaysNonWorking;
        private readonly IHolidayProvider _holidayProvider;
        private readonly Dictionary<MonthDay, string?> _recurringNonWorkingDays;
        private readonly Dictionary<DateTime, DateOverride> _dateOverrides;
        private readonly List<HolidayInfo> _customHolidays;
        private readonly bool _useJapanHolidays;
        private readonly DateTime? _minWorkingOverrideDate;
        private readonly DateTime? _maxWorkingOverrideDate;

        internal WorkCalendar(
            string? id,
            string? name,
            DailyWorkSchedule schedule,
            IEnumerable<DayOfWeek> nonWorkingDaysOfWeek,
            IHolidayProvider holidayProvider,
            bool useJapanHolidays,
            IDictionary<MonthDay, string?> recurringNonWorkingDays,
            IDictionary<DateTime, DateOverride> dateOverrides,
            IEnumerable<HolidayInfo> customHolidays)
        {
            Id = id;
            Name = name;
            Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _holidayProvider = holidayProvider ?? throw new ArgumentNullException(nameof(holidayProvider));
            _useJapanHolidays = useJapanHolidays;

            _nonWorkingDayFlags = new bool[7];
            var weekdayList = new List<DayOfWeek>();
            foreach (DayOfWeek dayOfWeek in nonWorkingDaysOfWeek)
            {
                int idx = (int)dayOfWeek;
                if (idx < 0 || idx > 6)
                {
                    throw new ArgumentOutOfRangeException(nameof(nonWorkingDaysOfWeek), dayOfWeek, "不正な曜日が指定されました。");
                }

                if (!_nonWorkingDayFlags[idx])
                {
                    _nonWorkingDayFlags[idx] = true;
                    weekdayList.Add(dayOfWeek);
                }
            }

            NonWorkingDaysOfWeek = new ReadOnlyCollection<DayOfWeek>(weekdayList.ToArray());

            bool allSeven = true;
            for (int i = 0; i < 7; i++)
            {
                if (!_nonWorkingDayFlags[i])
                {
                    allSeven = false;
                    break;
                }
            }

            _allSevenDaysNonWorking = allSeven;

            _recurringNonWorkingDays = new Dictionary<MonthDay, string?>(recurringNonWorkingDays);
            _dateOverrides = new Dictionary<DateTime, DateOverride>(dateOverrides.Count);

            DateTime? minWorking = null;
            DateTime? maxWorking = null;
            foreach (KeyValuePair<DateTime, DateOverride> entry in dateOverrides)
            {
                DateTime key = entry.Key.Date;
                DateOverride value = entry.Value;
                _dateOverrides[key] = value;

                if (value.IsWorkingDay)
                {
                    if (!minWorking.HasValue || key < minWorking.Value)
                    {
                        minWorking = key;
                    }

                    if (!maxWorking.HasValue || key > maxWorking.Value)
                    {
                        maxWorking = key;
                    }
                }
            }

            _minWorkingOverrideDate = minWorking;
            _maxWorkingOverrideDate = maxWorking;
            _customHolidays = new List<HolidayInfo>(customHolidays);
        }

        /// <inheritdoc />
        public string? Id { get; }

        /// <inheritdoc />
        public string? Name { get; }

        /// <inheritdoc />
        public DailyWorkSchedule Schedule { get; }

        /// <inheritdoc />
        public IReadOnlyCollection<DayOfWeek> NonWorkingDaysOfWeek { get; }

        /// <inheritdoc />
        public IHolidayProvider HolidayProvider => _holidayProvider;

        /// <summary>
        /// カレンダーを段階的に構築するための新しい <see cref="WorkCalendarBuilder"/> を作成します。
        /// </summary>
        /// <returns>新しい <see cref="WorkCalendarBuilder"/>。</returns>
        public static WorkCalendarBuilder CreateBuilder() => new WorkCalendarBuilder();

        /// <summary>
        /// 日本の標準的な既定カレンダー（月〜金 08:00-17:00・休憩 12:00-13:00、土日・日本の祝日非稼働）を作成します。
        /// </summary>
        /// <returns>既定設定の <see cref="WorkCalendar"/>。</returns>
        public static WorkCalendar CreateDefault() => CreateBuilder().Build();

        /// <summary>
        /// シリアライズや DB レコード等から取得した <see cref="WorkCalendarDefinition"/> より
        /// <see cref="WorkCalendar"/> を構築します。
        /// </summary>
        /// <param name="definition">カレンダー定義 DTO。</param>
        /// <param name="holidayProviderOverride">祝日プロバイダを外部から差し替える場合に指定します（省略可）。</param>
        /// <returns>構築された <see cref="WorkCalendar"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">定義内容に重複や不整合がある場合にスローされます。</exception>
        public static WorkCalendar FromDefinition(
            WorkCalendarDefinition definition,
            IHolidayProvider? holidayProviderOverride = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            DailyWorkSchedule schedule;
            if (definition.WorkingPeriods == null && definition.Breaks == null)
            {
                schedule = DailyWorkSchedule.StandardDayShift;
            }
            else
            {
                List<WorkTimeRange> periods;
                if (definition.WorkingPeriods == null)
                {
                    periods = new List<WorkTimeRange>
                    {
                        new WorkTimeRange(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                    };
                }
                else
                {
                    periods = new List<WorkTimeRange>(definition.WorkingPeriods.Count);
                    foreach (TimeRangeDefinition item in definition.WorkingPeriods)
                    {
                        if (item == null)
                        {
                            throw new ArgumentException("稼働時間帯定義に null 要素を含めることはできません。", nameof(definition));
                        }

                        TimeSpan start = TimeFormatHelper.ParseShiftTime(item.Start);
                        TimeSpan end = TimeFormatHelper.ParseShiftTime(item.End);
                        periods.Add(WorkTimeRange.FromClock(start, end));
                    }
                }

                List<WorkTimeRange>? breaks = null;
                if (definition.Breaks != null)
                {
                    breaks = new List<WorkTimeRange>(definition.Breaks.Count);
                    foreach (TimeRangeDefinition item in definition.Breaks)
                    {
                        if (item == null)
                        {
                            throw new ArgumentException("休憩時間帯定義に null 要素を含めることはできません。", nameof(definition));
                        }

                        TimeSpan start = TimeFormatHelper.ParseShiftTime(item.Start);
                        TimeSpan end = TimeFormatHelper.ParseShiftTime(item.End);
                        breaks.Add(WorkTimeRange.FromClock(start, end));
                    }
                }

                schedule = new DailyWorkSchedule(periods, breaks);
            }

            IEnumerable<DayOfWeek> nonWorkingDays = definition.NonWorkingDaysOfWeek ??
                (IEnumerable<DayOfWeek>)new[] { DayOfWeek.Saturday, DayOfWeek.Sunday };

            var recurring = new Dictionary<MonthDay, string?>();
            if (definition.RecurringNonWorkingDays != null)
            {
                foreach (RecurringHolidayDefinition item in definition.RecurringNonWorkingDays)
                {
                    if (item == null)
                    {
                        throw new ArgumentException("毎年固定休日定義に null 要素を含めることはできません。", nameof(definition));
                    }

                    var md = new MonthDay(item.Month, item.Day);
                    recurring[md] = item.Reason;
                }
            }

            var overrides = new Dictionary<DateTime, DateOverride>();
            if (definition.DateOverrides != null)
            {
                foreach (DateOverrideDefinition item in definition.DateOverrides)
                {
                    if (item == null)
                    {
                        throw new ArgumentException("日付上書き定義に null 要素を含めることはできません。", nameof(definition));
                    }

                    DateTime date = TimeFormatHelper.ParseDate(item.Date);
                    if (overrides.ContainsKey(date))
                    {
                        throw new ArgumentException(
                            $"日付 '{TimeFormatHelper.FormatDate(date)}' に対する上書き定義が重複しています。",
                            nameof(definition));
                    }

                    overrides.Add(date, new DateOverride(date, item.IsWorkingDay, item.Reason));
                }
            }

            var customHolidays = new List<HolidayInfo>();
            if (definition.CustomHolidays != null)
            {
                foreach (CustomHolidayDefinition item in definition.CustomHolidays)
                {
                    if (item == null)
                    {
                        throw new ArgumentException("独自祝日定義に null 要素を含めることはできません。", nameof(definition));
                    }

                    DateTime date = TimeFormatHelper.ParseDate(item.Date);
                    customHolidays.Add(new HolidayInfo(date, item.Name, HolidayKind.Custom));
                }
            }

            IHolidayProvider baseProvider = holidayProviderOverride ??
                (definition.UseJapanHolidays ? (IHolidayProvider)JapanHolidayProvider.Instance : NullHolidayProvider.Instance);

            IHolidayProvider effectiveProvider;
            if (customHolidays.Count > 0)
            {
                var customProvider = new InMemoryHolidayProvider(customHolidays);
                effectiveProvider = baseProvider is NullHolidayProvider
                    ? customProvider
                    : new CompositeHolidayProvider(customProvider, baseProvider);
            }
            else
            {
                effectiveProvider = baseProvider;
            }

            return new WorkCalendar(
                definition.Id,
                definition.Name,
                schedule,
                nonWorkingDays,
                effectiveProvider,
                definition.UseJapanHolidays,
                recurring,
                overrides,
                customHolidays);
        }

        /// <inheritdoc />
        public WorkCalendarDefinition ToDefinition()
        {
            var workingPeriods = new List<TimeRangeDefinition>(Schedule.WorkingPeriods.Count);
            foreach (WorkTimeRange range in Schedule.WorkingPeriods)
            {
                workingPeriods.Add(new TimeRangeDefinition
                {
                    Start = TimeFormatHelper.FormatShiftTime(range.Start),
                    End = TimeFormatHelper.FormatShiftTime(range.End)
                });
            }

            var breaks = new List<TimeRangeDefinition>(Schedule.Breaks.Count);
            foreach (WorkTimeRange brk in Schedule.Breaks)
            {
                breaks.Add(new TimeRangeDefinition
                {
                    Start = TimeFormatHelper.FormatShiftTime(brk.Start),
                    End = TimeFormatHelper.FormatShiftTime(brk.End)
                });
            }

            var nonWorkingWeekdays = new List<DayOfWeek>(NonWorkingDaysOfWeek);

            List<RecurringHolidayDefinition>? recurringList = null;
            if (_recurringNonWorkingDays.Count > 0)
            {
                var sortedKeys = new List<MonthDay>(_recurringNonWorkingDays.Keys);
                sortedKeys.Sort();
                recurringList = new List<RecurringHolidayDefinition>(sortedKeys.Count);
                foreach (MonthDay key in sortedKeys)
                {
                    recurringList.Add(new RecurringHolidayDefinition
                    {
                        Month = key.Month,
                        Day = key.Day,
                        Reason = _recurringNonWorkingDays[key]
                    });
                }
            }

            List<DateOverrideDefinition>? overrideList = null;
            if (_dateOverrides.Count > 0)
            {
                var sortedDates = new List<DateTime>(_dateOverrides.Keys);
                sortedDates.Sort();
                overrideList = new List<DateOverrideDefinition>(sortedDates.Count);
                foreach (DateTime dt in sortedDates)
                {
                    DateOverride ov = _dateOverrides[dt];
                    overrideList.Add(new DateOverrideDefinition
                    {
                        Date = TimeFormatHelper.FormatDate(ov.Date),
                        IsWorkingDay = ov.IsWorkingDay,
                        Reason = ov.Reason
                    });
                }
            }

            List<CustomHolidayDefinition>? customHolidayList = null;
            if (_customHolidays.Count > 0)
            {
                var sortedCustom = new List<HolidayInfo>(_customHolidays);
                sortedCustom.Sort((left, right) => left.Date.CompareTo(right.Date));
                customHolidayList = new List<CustomHolidayDefinition>(sortedCustom.Count);
                foreach (HolidayInfo info in sortedCustom)
                {
                    customHolidayList.Add(new CustomHolidayDefinition
                    {
                        Date = TimeFormatHelper.FormatDate(info.Date),
                        Name = info.Name
                    });
                }
            }

            return new WorkCalendarDefinition
            {
                Id = Id,
                Name = Name,
                WorkingPeriods = workingPeriods,
                Breaks = breaks,
                NonWorkingDaysOfWeek = nonWorkingWeekdays,
                UseJapanHolidays = _useJapanHolidays,
                RecurringNonWorkingDays = recurringList,
                DateOverrides = overrideList,
                CustomHolidays = customHolidayList
            };
        }

        /// <inheritdoc />
        public bool IsWorkingDay(DateTime date)
        {
            DateTime target = date.Date;

            // 1. 個別日付の特例上書き（最優先）
            if (_dateOverrides.TryGetValue(target, out DateOverride? dateOverride))
            {
                return dateOverride.IsWorkingDay;
            }

            // 2. 毎年固定の会社休日
            if (_recurringNonWorkingDays.ContainsKey(new MonthDay(target.Month, target.Day)))
            {
                return false;
            }

            // 3. 祝日プロバイダ
            if (_holidayProvider.TryGetHoliday(target, out _))
            {
                return false;
            }

            // 4. 定休曜日
            return !_nonWorkingDayFlags[(int)target.DayOfWeek];
        }

        /// <inheritdoc />
        public DayStatus GetDayStatus(DateTime date)
        {
            DateTime target = date.Date;

            if (_dateOverrides.TryGetValue(target, out DateOverride? dateOverride))
            {
                return new DayStatus(
                    target,
                    dateOverride.IsWorkingDay,
                    dateOverride.IsWorkingDay
                        ? DayStatusReason.OverrideWorkingDay
                        : DayStatusReason.OverrideNonWorkingDay,
                    dateOverride.Reason);
            }

            var monthDay = new MonthDay(target.Month, target.Day);
            if (_recurringNonWorkingDays.TryGetValue(monthDay, out string? recurringReason))
            {
                return new DayStatus(target, isWorkingDay: false, DayStatusReason.RecurringHoliday, recurringReason);
            }

            if (_holidayProvider.TryGetHoliday(target, out HolidayInfo? holiday) && holiday != null)
            {
                return new DayStatus(target, isWorkingDay: false, DayStatusReason.PublicHoliday, holiday.Name);
            }

            if (_nonWorkingDayFlags[(int)target.DayOfWeek])
            {
                return new DayStatus(target, isWorkingDay: false, DayStatusReason.Weekend);
            }

            return new DayStatus(target, isWorkingDay: true, DayStatusReason.RegularWorkingDay);
        }

        /// <inheritdoc />
        public DateTime AddWorkingDays(DateTime date, int workingDays)
        {
            DateTime current = date.Date;
            if (workingDays == 0)
            {
                return DateTime.SpecifyKind(current, date.Kind);
            }

            int direction = workingDays > 0 ? 1 : -1;
            int remaining = workingDays > 0 ? workingDays : -workingDays;

            EnsureCanMoveInDirection(current, direction);

            int skippedConsecutive = 0;
            while (remaining > 0)
            {
                if (direction > 0 && current >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(workingDays), "計算結果が DateTime.MaxValue を超えました。");
                }

                if (direction < 0 && current <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(workingDays), "計算結果が DateTime.MinValue を下回りました。");
                }

                current = current.AddDays(direction);
                if (IsWorkingDay(current))
                {
                    remaining--;
                    skippedConsecutive = 0;
                }
                else
                {
                    skippedConsecutive++;
                    CheckSearchLimit(current, direction, skippedConsecutive);
                }
            }

            return DateTime.SpecifyKind(current, date.Kind);
        }

        /// <inheritdoc />
        public DateTime GetNextWorkingDay(DateTime date, bool includeSelf = true)
        {
            DateTime current = date.Date;
            if (!includeSelf)
            {
                if (current >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(date), "探索範囲が DateTime.MaxValue を超えました。");
                }

                current = current.AddDays(1);
            }

            EnsureCanMoveInDirection(current, direction: 1);

            int skipped = 0;
            while (!IsWorkingDay(current))
            {
                skipped++;
                CheckSearchLimit(current, direction: 1, skipped);

                if (current >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(date), "探索範囲が DateTime.MaxValue を超えました。");
                }

                current = current.AddDays(1);
            }

            return DateTime.SpecifyKind(current, date.Kind);
        }

        /// <inheritdoc />
        public DateTime GetPreviousWorkingDay(DateTime date, bool includeSelf = true)
        {
            DateTime current = date.Date;
            if (!includeSelf)
            {
                if (current <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(date), "探索範囲が DateTime.MinValue を下回りました。");
                }

                current = current.AddDays(-1);
            }

            EnsureCanMoveInDirection(current, direction: -1);

            int skipped = 0;
            while (!IsWorkingDay(current))
            {
                skipped++;
                CheckSearchLimit(current, direction: -1, skipped);

                if (current <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(date), "探索範囲が DateTime.MinValue を下回りました。");
                }

                current = current.AddDays(-1);
            }

            return DateTime.SpecifyKind(current, date.Kind);
        }

        /// <inheritdoc />
        public int CountWorkingDays(DateTime startDate, DateTime endDate)
        {
            DateTime start = startDate.Date;
            DateTime end = endDate.Date;
            if (end < start)
            {
                throw new ArgumentOutOfRangeException(nameof(endDate), endDate, "終了日は開始日以降である必要があります。");
            }

            int count = 0;
            for (DateTime day = start; ; day = day.AddDays(1))
            {
                if (IsWorkingDay(day))
                {
                    count++;
                }

                if (day >= end)
                {
                    break;
                }
            }

            return count;
        }

        /// <inheritdoc />
        public IEnumerable<DateTime> GetWorkingDays(DateTime startDate, DateTime endDate)
        {
            DateTime start = startDate.Date;
            DateTime end = endDate.Date;
            if (end < start)
            {
                throw new ArgumentOutOfRangeException(nameof(endDate), endDate, "終了日は開始日以降である必要があります。");
            }

            return EnumerateWorkingDays(start, end, startDate.Kind);
        }

#if NET6_0_OR_GREATER
        /// <inheritdoc />
        public bool IsWorkingDay(DateOnly date)
        {
            return IsWorkingDay(date.ToDateTime(TimeOnly.MinValue));
        }

        /// <inheritdoc />
        public DayStatus GetDayStatus(DateOnly date)
        {
            return GetDayStatus(date.ToDateTime(TimeOnly.MinValue));
        }

        /// <inheritdoc />
        public DateOnly AddWorkingDays(DateOnly date, int workingDays)
        {
            return DateOnly.FromDateTime(AddWorkingDays(date.ToDateTime(TimeOnly.MinValue), workingDays));
        }

        /// <inheritdoc />
        public DateOnly GetNextWorkingDay(DateOnly date, bool includeSelf = true)
        {
            return DateOnly.FromDateTime(GetNextWorkingDay(date.ToDateTime(TimeOnly.MinValue), includeSelf));
        }

        /// <inheritdoc />
        public DateOnly GetPreviousWorkingDay(DateOnly date, bool includeSelf = true)
        {
            return DateOnly.FromDateTime(GetPreviousWorkingDay(date.ToDateTime(TimeOnly.MinValue), includeSelf));
        }

        /// <inheritdoc />
        public int CountWorkingDays(DateOnly startDate, DateOnly endDate)
        {
            return CountWorkingDays(
                startDate.ToDateTime(TimeOnly.MinValue),
                endDate.ToDateTime(TimeOnly.MinValue));
        }

        /// <inheritdoc />
        public IEnumerable<DateOnly> GetWorkingDays(DateOnly startDate, DateOnly endDate)
        {
            DateTime start = startDate.ToDateTime(TimeOnly.MinValue);
            DateTime end = endDate.ToDateTime(TimeOnly.MinValue);
            if (end < start)
            {
                throw new ArgumentOutOfRangeException(nameof(endDate), endDate, "終了日は開始日以降である必要があります。");
            }

            return EnumerateWorkingDateOnly(start, end);
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateOnly workingDate)
        {
            return GetShiftIntervals(workingDate.ToDateTime(TimeOnly.MinValue));
        }
#endif

        /// <inheritdoc />
        public bool IsWorkingTime(DateTime dateTime)
        {
            WorkTimeRange[] slices = GetSlicesForDate(dateTime.Date);
            TimeSpan tod = dateTime.TimeOfDay;
            for (int i = 0; i < slices.Length; i++)
            {
                if (slices[i].Contains(tod))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public DateTime AddWorkingTime(DateTime startDateTime, TimeSpan duration)
        {
            if (duration == TimeSpan.Zero)
            {
                return startDateTime;
            }

            if (duration < TimeSpan.Zero)
            {
                if (duration == TimeSpan.MinValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration), duration, "TimeSpan.MinValue は指定できません。");
                }

                return SubtractWorkingTime(startDateTime, duration.Negate());
            }

            long remainingTicks = duration.Ticks;
            DateTime currentDay = startDateTime.Date;
            TimeSpan currentTod = startDateTime.TimeOfDay;

            EnsureCanMoveTimeInDirection(currentDay, direction: 1);

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay);
                bool hadSliceAfterCursor = false;

                for (int i = 0; i < slices.Length; i++)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.End <= currentTod)
                    {
                        continue;
                    }

                    hadSliceAfterCursor = true;
                    TimeSpan effectiveStart = slice.Start > currentTod ? slice.Start : currentTod;
                    long availableTicks = (slice.End - effectiveStart).Ticks;

                    if (remainingTicks <= availableTicks)
                    {
                        TimeSpan resultOffset = effectiveStart + TimeSpan.FromTicks(remainingTicks);
                        if (currentDay == MaxSearchDate && resultOffset >= TimeFormatHelper.OneDay)
                        {
                            throw new ArgumentOutOfRangeException(nameof(duration), "計算結果が DateTime.MaxValue を超えました。");
                        }

                        return DateTime.SpecifyKind(currentDay.Add(resultOffset), startDateTime.Kind);
                    }

                    remainingTicks -= availableTicks;
                }

                if (hadSliceAfterCursor)
                {
                    emptyDays = 0;
                }
                else
                {
                    emptyDays++;
                    CheckTimeSearchLimit(currentDay, direction: 1, emptyDays);
                }

                if (currentDay >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration), "計算結果が DateTime.MaxValue を超えました。");
                }

                currentDay = currentDay.AddDays(1);
                currentTod = TimeSpan.Zero;
            }
        }

        /// <inheritdoc />
        public DateTime SubtractWorkingTime(DateTime endDateTime, TimeSpan duration)
        {
            if (duration == TimeSpan.Zero)
            {
                return endDateTime;
            }

            if (duration < TimeSpan.Zero)
            {
                if (duration == TimeSpan.MinValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration), duration, "TimeSpan.MinValue は指定できません。");
                }

                return AddWorkingTime(endDateTime, duration.Negate());
            }

            long remainingTicks = duration.Ticks;
            DateTime currentDay = endDateTime.Date;
            TimeSpan currentTod = endDateTime.TimeOfDay;

            EnsureCanMoveTimeInDirection(currentDay, direction: -1);

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay);
                bool hadSliceBeforeCursor = false;

                for (int i = slices.Length - 1; i >= 0; i--)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.Start >= currentTod)
                    {
                        continue;
                    }

                    hadSliceBeforeCursor = true;
                    TimeSpan effectiveEnd = slice.End < currentTod ? slice.End : currentTod;
                    long availableTicks = (effectiveEnd - slice.Start).Ticks;

                    if (remainingTicks <= availableTicks)
                    {
                        TimeSpan resultOffset = effectiveEnd - TimeSpan.FromTicks(remainingTicks);
                        return DateTime.SpecifyKind(currentDay.Add(resultOffset), endDateTime.Kind);
                    }

                    remainingTicks -= availableTicks;
                }

                if (hadSliceBeforeCursor)
                {
                    emptyDays = 0;
                }
                else
                {
                    emptyDays++;
                    CheckTimeSearchLimit(currentDay, direction: -1, emptyDays);
                }

                if (currentDay <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration), "計算結果が DateTime.MinValue を下回りました。");
                }

                currentDay = currentDay.AddDays(-1);
                currentTod = TimeFormatHelper.OneDay;
            }
        }

        /// <inheritdoc />
        public TimeSpan GetWorkingTime(DateTime startDateTime, DateTime endDateTime)
        {
            if (startDateTime == endDateTime)
            {
                return TimeSpan.Zero;
            }

            if (endDateTime < startDateTime)
            {
                return GetWorkingTime(endDateTime, startDateTime).Negate();
            }

            DateTime startDay = startDateTime.Date;
            DateTime endDay = endDateTime.Date;
            long totalTicks = 0;

            for (DateTime day = startDay; ; day = day.AddDays(1))
            {
                TimeSpan windowStart = day == startDay ? startDateTime.TimeOfDay : TimeSpan.Zero;
                TimeSpan windowEnd = day == endDay ? endDateTime.TimeOfDay : TimeFormatHelper.OneDay;

                if (windowEnd > windowStart)
                {
                    WorkTimeRange[] slices = GetSlicesForDate(day);
                    for (int i = 0; i < slices.Length; i++)
                    {
                        WorkTimeRange slice = slices[i];
                        TimeSpan overlapStart = slice.Start > windowStart ? slice.Start : windowStart;
                        TimeSpan overlapEnd = slice.End < windowEnd ? slice.End : windowEnd;
                        if (overlapEnd > overlapStart)
                        {
                            totalTicks += (overlapEnd - overlapStart).Ticks;
                        }
                    }
                }

                if (day >= endDay)
                {
                    break;
                }
            }

            return new TimeSpan(totalTicks);
        }

        /// <inheritdoc />
        public DateTime GetNextWorkingTime(DateTime dateTime)
        {
            if (IsWorkingTime(dateTime))
            {
                return dateTime;
            }

            DateTime currentDay = dateTime.Date;
            TimeSpan currentTod = dateTime.TimeOfDay;

            EnsureCanMoveTimeInDirection(currentDay, direction: 1);

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay);
                for (int i = 0; i < slices.Length; i++)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.Start >= currentTod)
                    {
                        return DateTime.SpecifyKind(currentDay.Add(slice.Start), dateTime.Kind);
                    }
                }

                emptyDays++;
                CheckTimeSearchLimit(currentDay, direction: 1, emptyDays);

                if (currentDay >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(dateTime), "探索範囲が DateTime.MaxValue を超えました。");
                }

                currentDay = currentDay.AddDays(1);
                currentTod = TimeSpan.Zero;
            }
        }

        /// <inheritdoc />
        public DateTime GetPreviousWorkingTime(DateTime dateTime)
        {
            if (IsWorkingTime(dateTime))
            {
                return dateTime;
            }

            DateTime currentDay = dateTime.Date;
            TimeSpan currentTod = dateTime.TimeOfDay;

            EnsureCanMoveTimeInDirection(currentDay, direction: -1);

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay);
                for (int i = slices.Length - 1; i >= 0; i--)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.Start < currentTod)
                    {
                        TimeSpan candidateTod = slice.End < currentTod ? slice.End : currentTod;
                        return DateTime.SpecifyKind(currentDay.Add(candidateTod), dateTime.Kind);
                    }
                }

                emptyDays++;
                CheckTimeSearchLimit(currentDay, direction: -1, emptyDays);

                if (currentDay <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(dateTime), "探索範囲が DateTime.MinValue を下回りました。");
                }

                currentDay = currentDay.AddDays(-1);
                currentTod = TimeFormatHelper.OneDay;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetWorkingIntervals(DateTime startDateTime, DateTime endDateTime)
        {
            if (endDateTime < startDateTime)
            {
                throw new ArgumentOutOfRangeException(nameof(endDateTime), endDateTime, "終了日時は開始日時以降である必要があります。");
            }

            if (endDateTime == startDateTime)
            {
                return Array.Empty<DateTimeInterval>();
            }

            DateTime startDay = startDateTime.Date;
            DateTime endDay = endDateTime.Date;
            DateTimeKind kind = startDateTime.Kind;
            var intervals = new List<DateTimeInterval>();

            for (DateTime day = startDay; ; day = day.AddDays(1))
            {
                TimeSpan windowStart = day == startDay ? startDateTime.TimeOfDay : TimeSpan.Zero;
                TimeSpan windowEnd = day == endDay ? endDateTime.TimeOfDay : TimeFormatHelper.OneDay;

                if (windowEnd > windowStart)
                {
                    WorkTimeRange[] slices = GetSlicesForDate(day);
                    for (int i = 0; i < slices.Length; i++)
                    {
                        WorkTimeRange slice = slices[i];
                        TimeSpan overlapStart = slice.Start > windowStart ? slice.Start : windowStart;
                        TimeSpan overlapEnd = slice.End < windowEnd ? slice.End : windowEnd;
                        if (overlapEnd > overlapStart)
                        {
                            DateTime absStart = DateTime.SpecifyKind(day.Add(overlapStart), kind);
                            DateTime absEnd = DateTime.SpecifyKind(day.Add(overlapEnd), kind);

                            if (intervals.Count > 0 && intervals[intervals.Count - 1].End == absStart)
                            {
                                intervals[intervals.Count - 1] = new DateTimeInterval(
                                    intervals[intervals.Count - 1].Start,
                                    absEnd);
                            }
                            else
                            {
                                intervals.Add(new DateTimeInterval(absStart, absEnd));
                            }
                        }
                    }
                }

                if (day >= endDay)
                {
                    break;
                }
            }

            return new ReadOnlyCollection<DateTimeInterval>(intervals.ToArray());
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateTime workingDate)
        {
            DateTime target = workingDate.Date;
            if (!IsWorkingDay(target))
            {
                return Array.Empty<DateTimeInterval>();
            }

            IReadOnlyList<WorkTimeRange> ranges = Schedule.EffectiveWorkingRanges;
            var result = new DateTimeInterval[ranges.Count];
            DateTimeKind kind = workingDate.Kind;

            for (int i = 0; i < ranges.Count; i++)
            {
                WorkTimeRange range = ranges[i];
                result[i] = new DateTimeInterval(
                    DateTime.SpecifyKind(target.Add(range.Start), kind),
                    DateTime.SpecifyKind(target.Add(range.End), kind));
            }

            return new ReadOnlyCollection<DateTimeInterval>(result);
        }

        private WorkTimeRange[] GetSlicesForDate(DateTime date)
        {
            bool isCurrentWorking = IsWorkingDay(date);
            bool isPreviousWorking = date > MinSearchDate && IsWorkingDay(date.AddDays(-1));
            return Schedule.GetDaySlices(isPreviousWorking, isCurrentWorking);
        }

        private IEnumerable<DateTime> EnumerateWorkingDays(DateTime start, DateTime end, DateTimeKind kind)
        {
            for (DateTime day = start; ; day = day.AddDays(1))
            {
                if (IsWorkingDay(day))
                {
                    yield return DateTime.SpecifyKind(day, kind);
                }

                if (day >= end)
                {
                    break;
                }
            }
        }

#if NET6_0_OR_GREATER
        private IEnumerable<DateOnly> EnumerateWorkingDateOnly(DateTime start, DateTime end)
        {
            for (DateTime day = start; ; day = day.AddDays(1))
            {
                if (IsWorkingDay(day))
                {
                    yield return DateOnly.FromDateTime(day);
                }

                if (day >= end)
                {
                    break;
                }
            }
        }
#endif

        private void EnsureCanMoveInDirection(DateTime startDay, int direction)
        {
            if (!_allSevenDaysNonWorking)
            {
                return;
            }

            if (!_maxWorkingOverrideDate.HasValue || !_minWorkingOverrideDate.HasValue)
            {
                throw new InvalidOperationException("すべての曜日が非稼働に設定されており、稼働日の上書きもないため稼働日を特定できません。");
            }

            if (direction > 0 && startDay > _maxWorkingOverrideDate.Value)
            {
                throw new InvalidOperationException("指定日以降に稼働可能日が存在しないため、稼働日を算出できません。");
            }

            if (direction < 0 && startDay < _minWorkingOverrideDate.Value)
            {
                throw new InvalidOperationException("指定日以前に稼働可能日が存在しないため、稼働日を算出できません。");
            }
        }

        private void EnsureCanMoveTimeInDirection(DateTime startDay, int direction)
        {
            if (!_allSevenDaysNonWorking)
            {
                return;
            }

            if (!_maxWorkingOverrideDate.HasValue || !_minWorkingOverrideDate.HasValue)
            {
                throw new InvalidOperationException("すべての曜日が非稼働に設定されており、稼働日の上書きもないため稼働時間を特定できません。");
            }

            DateTime effectiveMax = Schedule.HasOvernightSpillover && _maxWorkingOverrideDate.Value < MaxSearchDate
                ? _maxWorkingOverrideDate.Value.AddDays(1)
                : _maxWorkingOverrideDate.Value;

            if (direction > 0 && startDay > effectiveMax)
            {
                throw new InvalidOperationException("指定日時以降に稼働時間帯が存在しないため、計算を完了できません。");
            }

            if (direction < 0 && startDay < _minWorkingOverrideDate.Value)
            {
                throw new InvalidOperationException("指定日時以前に稼働時間帯が存在しないため、計算を完了できません。");
            }
        }

        private void CheckSearchLimit(DateTime currentDay, int direction, int consecutiveSkipped)
        {
            if (_allSevenDaysNonWorking)
            {
                if (direction > 0 && (!_maxWorkingOverrideDate.HasValue || currentDay >= _maxWorkingOverrideDate.Value))
                {
                    throw new InvalidOperationException("指定日以降に十分な稼働日が存在しないため、計算を完了できません。");
                }

                if (direction < 0 && (!_minWorkingOverrideDate.HasValue || currentDay <= _minWorkingOverrideDate.Value))
                {
                    throw new InvalidOperationException("指定日以前に十分な稼働日が存在しないため、計算を完了できません。");
                }
            }

            if (consecutiveSkipped > MaxConsecutiveNonWorkingDays)
            {
                throw new InvalidOperationException(
                    $"連続 {MaxConsecutiveNonWorkingDays} 日を超える非稼働期間が検出されたため、探索を中断しました。カレンダー設定を確認してください。");
            }
        }

        private void CheckTimeSearchLimit(DateTime currentDay, int direction, int consecutiveEmptyDays)
        {
            if (_allSevenDaysNonWorking)
            {
                DateTime effectiveMax = _maxWorkingOverrideDate.HasValue
                    ? (Schedule.HasOvernightSpillover && _maxWorkingOverrideDate.Value < MaxSearchDate
                        ? _maxWorkingOverrideDate.Value.AddDays(1)
                        : _maxWorkingOverrideDate.Value)
                    : MinSearchDate;

                if (direction > 0 && (!_maxWorkingOverrideDate.HasValue || currentDay >= effectiveMax))
                {
                    throw new InvalidOperationException("指定日時以降に十分な稼働時間が存在しないため、計算を完了できません。");
                }

                if (direction < 0 && (!_minWorkingOverrideDate.HasValue || currentDay <= _minWorkingOverrideDate.Value))
                {
                    throw new InvalidOperationException("指定日時以前に十分な稼働時間が存在しないため、計算を完了できません。");
                }
            }

            if (consecutiveEmptyDays > MaxConsecutiveNonWorkingDays)
            {
                throw new InvalidOperationException(
                    $"連続 {MaxConsecutiveNonWorkingDays} 日を超える非稼働期間が検出されたため、探索を中断しました。カレンダー設定を確認してください。");
            }
        }
    }
}
