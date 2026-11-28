using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WorkTimeCalc.Holidays;
using WorkTimeCalc.Internal;
using WorkTimeCalc.Models;
using WorkTimeCalc.Serialization;

namespace WorkTimeCalc
{
    /// <summary>
    /// 稼働日判定・稼働日計算・実稼働時間計算を行うイミュータブルかつスレッドセーフな稼働カレンダーです。
    /// 日勤・複数休憩・日跨ぎ稼働（夜勤）・休日出勤上書き・日本の祝日判定をサポートします。
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
            if (definition.WorkingPeriods == null && definition.Breaks == null && definition.OvertimePeriods == null)
            {
                schedule = DailyWorkSchedule.StandardDayShift;
            }
            else
            {
                List<WorkTimeRange> periods = ParseRangeList(
                    definition.WorkingPeriods,
                    "稼働時間帯定義に null 要素を含めることはできません。",
                    nameof(definition)) ??
                    new List<WorkTimeRange> { new WorkTimeRange(TimeSpan.FromHours(8), TimeSpan.FromHours(17)) };

                List<WorkTimeRange>? breaks = ParseRangeList(
                    definition.Breaks,
                    "休憩時間帯定義に null 要素を含めることはできません。",
                    nameof(definition));

                if (definition.WorkingPeriods == null && breaks == null)
                {
                    breaks = new List<WorkTimeRange> { new WorkTimeRange(TimeSpan.FromHours(12), TimeSpan.FromHours(13)) };
                }

                List<WorkTimeRange>? overtimePeriods = ParseRangeList(
                    definition.OvertimePeriods,
                    "残業時間帯定義に null 要素を含めることはできません。",
                    nameof(definition));

                schedule = new DailyWorkSchedule(periods, breaks, overtimePeriods);
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

            List<TimeRangeDefinition>? overtimePeriods = null;
            if (Schedule.OvertimePeriods.Count > 0)
            {
                overtimePeriods = new List<TimeRangeDefinition>(Schedule.OvertimePeriods.Count);
                foreach (WorkTimeRange ot in Schedule.OvertimePeriods)
                {
                    overtimePeriods.Add(new TimeRangeDefinition
                    {
                        Start = TimeFormatHelper.FormatShiftTime(ot.Start),
                        End = TimeFormatHelper.FormatShiftTime(ot.End)
                    });
                }
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
                OvertimePeriods = overtimePeriods,
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

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateOnly workingDate, OvertimePolicy overtimePolicy)
        {
            return GetShiftIntervals(workingDate.ToDateTime(TimeOnly.MinValue), overtimePolicy);
        }
#endif

        /// <inheritdoc />
        public bool IsWorkingTime(DateTime dateTime)
        {
            return IsWorkingTime(dateTime, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public bool IsOvertime(DateTime dateTime)
        {
            if (!Schedule.HasOvertime)
            {
                return false;
            }

            if (IsWorkingTimeCore(dateTime, Schedule.RegularSliceSet))
            {
                return false;
            }

            return IsWorkingTimeCore(dateTime, Schedule.OvertimeOnlySliceSet);
        }

        /// <inheritdoc />
        public bool IsWorkingTime(DateTime dateTime, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                return IsInOrAfterActiveShift(dateTime) &&
                       IsWorkingTimeCore(dateTime, Schedule.UnlimitedSliceSet);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return IsWorkingTimeCore(dateTime, sliceSet);
        }

        /// <inheritdoc />
        public DateTime AddWorkingTime(DateTime startDateTime, TimeSpan duration)
        {
            return AddWorkingTime(startDateTime, duration, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public DateTime AddWorkingTime(DateTime startDateTime, TimeSpan duration, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
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

                return SubtractWorkingTime(startDateTime, duration.Negate(), overtimePolicy);
            }

            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                DateTime anchor = IsInOrAfterActiveShift(startDateTime)
                    ? startDateTime
                    : GetNextWorkingTimeCore(startDateTime, Schedule.RegularSliceSet);

                return AddWorkingTimeCore(
                    anchor,
                    duration,
                    Schedule.UnlimitedSliceSet,
                    checkWorkingDayLimits: !Schedule.HasUnlimitedNonWorkingDaySlices);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return AddWorkingTimeCore(startDateTime, duration, sliceSet, checkWorkingDayLimits: true);
        }

        /// <inheritdoc />
        public DateTime SubtractWorkingTime(DateTime endDateTime, TimeSpan duration)
        {
            return SubtractWorkingTime(endDateTime, duration, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public DateTime SubtractWorkingTime(DateTime endDateTime, TimeSpan duration, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
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

                return AddWorkingTime(endDateTime, duration.Negate(), overtimePolicy);
            }

            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                return SubtractWorkingTimeUnlimited(endDateTime, duration);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return SubtractWorkingTimeCore(endDateTime, duration, sliceSet, checkWorkingDayLimits: true);
        }

        /// <inheritdoc />
        public TimeSpan GetWorkingTime(DateTime startDateTime, DateTime endDateTime)
        {
            return GetWorkingTime(startDateTime, endDateTime, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public TimeSpan GetWorkingTime(DateTime startDateTime, DateTime endDateTime, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (startDateTime == endDateTime)
            {
                return TimeSpan.Zero;
            }

            if (endDateTime < startDateTime)
            {
                return GetWorkingTime(endDateTime, startDateTime, overtimePolicy).Negate();
            }

            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                if (!TryFindUnlimitedAnchor(startDateTime, endDateTime, out DateTime anchor))
                {
                    return TimeSpan.Zero;
                }

                return GetWorkingTimeCore(anchor, endDateTime, Schedule.UnlimitedSliceSet);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return GetWorkingTimeCore(startDateTime, endDateTime, sliceSet);
        }

        /// <inheritdoc />
        public WorkingTimeBreakdown GetWorkingTimeBreakdown(DateTime startDateTime, DateTime endDateTime)
        {
            return GetWorkingTimeBreakdown(startDateTime, endDateTime, OvertimePolicy.CalendarDefined);
        }

        /// <inheritdoc />
        public WorkingTimeBreakdown GetWorkingTimeBreakdown(
            DateTime startDateTime,
            DateTime endDateTime,
            OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (startDateTime == endDateTime)
            {
                return WorkingTimeBreakdown.Zero;
            }

            if (endDateTime < startDateTime)
            {
                return GetWorkingTimeBreakdown(endDateTime, startDateTime, overtimePolicy).Negate();
            }

            TimeSpan regular = GetWorkingTimeCore(startDateTime, endDateTime, Schedule.RegularSliceSet);
            if (overtimePolicy.Kind == OvertimePolicyKind.None)
            {
                return new WorkingTimeBreakdown(regular, TimeSpan.Zero);
            }

            TimeSpan total = GetWorkingTime(startDateTime, endDateTime, overtimePolicy);
            return new WorkingTimeBreakdown(regular, total - regular);
        }

        /// <inheritdoc />
        public DateTime GetNextWorkingTime(DateTime dateTime)
        {
            return GetNextWorkingTime(dateTime, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public DateTime GetNextWorkingTime(DateTime dateTime, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                if (!IsInOrAfterActiveShift(dateTime))
                {
                    return GetNextWorkingTimeCore(dateTime, Schedule.RegularSliceSet);
                }

                return GetNextWorkingTimeCore(
                    dateTime,
                    Schedule.UnlimitedSliceSet,
                    checkWorkingDayLimits: !Schedule.HasUnlimitedNonWorkingDaySlices);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return GetNextWorkingTimeCore(dateTime, sliceSet);
        }

        /// <inheritdoc />
        public DateTime GetPreviousWorkingTime(DateTime dateTime)
        {
            return GetPreviousWorkingTime(dateTime, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public DateTime GetPreviousWorkingTime(DateTime dateTime, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                if (!IsInOrAfterActiveShift(dateTime))
                {
                    return GetPreviousWorkingTimeCore(dateTime, Schedule.RegularSliceSet);
                }

                return GetPreviousWorkingTimeCore(
                    dateTime,
                    Schedule.UnlimitedSliceSet,
                    checkWorkingDayLimits: !Schedule.HasUnlimitedNonWorkingDaySlices);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return GetPreviousWorkingTimeCore(dateTime, sliceSet);
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetWorkingIntervals(DateTime startDateTime, DateTime endDateTime)
        {
            return GetWorkingIntervals(startDateTime, endDateTime, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetWorkingIntervals(
            DateTime startDateTime,
            DateTime endDateTime,
            OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (endDateTime < startDateTime)
            {
                throw new ArgumentOutOfRangeException(nameof(endDateTime), endDateTime, "終了日時は開始日時以降である必要があります。");
            }

            if (endDateTime == startDateTime)
            {
                return Array.Empty<DateTimeInterval>();
            }

            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                if (!TryFindUnlimitedAnchor(startDateTime, endDateTime, out DateTime anchor))
                {
                    return Array.Empty<DateTimeInterval>();
                }

                return GetWorkingIntervalsCore(anchor, endDateTime, Schedule.UnlimitedSliceSet);
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            return GetWorkingIntervalsCore(startDateTime, endDateTime, sliceSet);
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateTime workingDate)
        {
            return GetShiftIntervals(workingDate, OvertimePolicy.None);
        }

        /// <inheritdoc />
        public IReadOnlyList<DateTimeInterval> GetShiftIntervals(DateTime workingDate, OvertimePolicy overtimePolicy)
        {
            ValidatePolicy(overtimePolicy);
            if (overtimePolicy.Kind == OvertimePolicyKind.Unlimited)
            {
                throw new ArgumentException(
                    "OvertimePolicy.Unlimited は上限なしのため、有限のシフト区間を算出できません。",
                    nameof(overtimePolicy));
            }

            DateTime target = workingDate.Date;
            if (!IsWorkingDay(target))
            {
                return Array.Empty<DateTimeInterval>();
            }

            DailyWorkSchedule.DaySliceSet sliceSet = ResolveSliceSet(overtimePolicy);
            DailyWorkSchedule.RawSpan[] spans = sliceSet.EffectiveSpans;
            var result = new DateTimeInterval[spans.Length];
            DateTimeKind kind = workingDate.Kind;

            for (int i = 0; i < spans.Length; i++)
            {
                DailyWorkSchedule.RawSpan span = spans[i];
                result[i] = new DateTimeInterval(
                    DateTime.SpecifyKind(target.Add(span.Start), kind),
                    DateTime.SpecifyKind(target.Add(span.End), kind));
            }

            return new ReadOnlyCollection<DateTimeInterval>(result);
        }

        private static void ValidatePolicy(OvertimePolicy overtimePolicy)
        {
            switch (overtimePolicy.Kind)
            {
                case OvertimePolicyKind.None:
                case OvertimePolicyKind.CalendarDefined:
                case OvertimePolicyKind.MaxPerDay:
                case OvertimePolicyKind.Unlimited:
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(overtimePolicy),
                        overtimePolicy.Kind,
                        "不正な OvertimePolicyKind が指定されました。");
            }
        }

        private DailyWorkSchedule.DaySliceSet ResolveSliceSet(OvertimePolicy overtimePolicy)
        {
            return overtimePolicy.Kind switch
            {
                OvertimePolicyKind.None => Schedule.RegularSliceSet,
                OvertimePolicyKind.CalendarDefined => Schedule.CalendarOvertimeSliceSet,
                OvertimePolicyKind.MaxPerDay => Schedule.BuildDynamicOvertimeSliceSet(overtimePolicy.MaxDurationPerDay ?? TimeSpan.Zero),
                _ => throw new ArgumentOutOfRangeException(nameof(overtimePolicy))
            };
        }

        private bool IsInOrAfterActiveShift(DateTime dateTime)
        {
            DateTime day = dateTime.Date;
            TimeSpan tod = dateTime.TimeOfDay;
            TimeSpan previousShiftStartOnDay = Schedule.FirstRegularStart - TimeFormatHelper.OneDay;
            return (IsWorkingDay(day) && tod >= Schedule.FirstRegularStart) ||
                   (day > MinSearchDate &&
                    IsWorkingDay(day.AddDays(-1)) &&
                    Schedule.HasOvernightSpillover &&
                    tod >= previousShiftStartOnDay);
        }

        private DateTime SubtractWorkingTimeUnlimited(DateTime endDateTime, TimeSpan duration)
        {
            DateTime? minStartAnchor = null;
            if (_allSevenDaysNonWorking)
            {
                if (!_minWorkingOverrideDate.HasValue)
                {
                    throw new InvalidOperationException("すべての曜日が非稼働に設定されており、稼働日の上書きもないため稼働時間を特定できません。");
                }

                DateTime minOverride = _minWorkingOverrideDate.Value;
                if (minOverride.Ticks > DateTime.MaxValue.Ticks - Schedule.FirstRegularStart.Ticks)
                {
                    throw new InvalidOperationException("指定された終了日時と稼働時間を満たす有効な始業開始日時が存在しません。");
                }

                DateTime earliestStart = DateTime.SpecifyKind(minOverride.Add(Schedule.FirstRegularStart), endDateTime.Kind);
                if (endDateTime <= earliestStart)
                {
                    throw new InvalidOperationException("指定日時以前に十分な稼働時間が存在しないため、計算を完了できません。");
                }

                minStartAnchor = earliestStart;
            }

            DateTime candidate = SubtractWorkingTimeCore(
                endDateTime,
                duration,
                Schedule.UnlimitedSliceSet,
                checkWorkingDayLimits: !Schedule.HasUnlimitedNonWorkingDaySlices,
                minBoundaryDateTime: minStartAnchor);

            if (!IsInOrAfterActiveShift(candidate))
            {
                throw new InvalidOperationException("指定された終了日時と稼働時間を満たす有効な始業開始日時が存在しません。");
            }

            return candidate;
        }

        private bool TryFindUnlimitedAnchor(DateTime startDateTime, DateTime endDateTime, out DateTime anchor)
        {
            if (IsInOrAfterActiveShift(startDateTime))
            {
                anchor = startDateTime;
                return anchor < endDateTime;
            }

            DateTime startDay = startDateTime.Date;
            DateTime endDay = endDateTime.Date;
            for (DateTime day = startDay; ; day = day.AddDays(1))
            {
                TimeSpan windowStart = day == startDay ? startDateTime.TimeOfDay : TimeSpan.Zero;
                WorkTimeRange[] slices = GetSlicesForDate(day, Schedule.RegularSliceSet);
                for (int i = 0; i < slices.Length; i++)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.End <= windowStart)
                    {
                        continue;
                    }

                    TimeSpan effectiveStart = slice.Start > windowStart ? slice.Start : windowStart;
                    DateTime candidate = DateTime.SpecifyKind(day.Add(effectiveStart), startDateTime.Kind);
                    if (candidate < endDateTime)
                    {
                        anchor = candidate;
                        return true;
                    }

                    anchor = default;
                    return false;
                }

                if (day >= endDay)
                {
                    break;
                }
            }

            anchor = default;
            return false;
        }

        private bool IsWorkingTimeCore(DateTime dateTime, DailyWorkSchedule.DaySliceSet sliceSet)
        {
            WorkTimeRange[] slices = GetSlicesForDate(dateTime.Date, sliceSet);
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

        private DateTime AddWorkingTimeCore(
            DateTime startDateTime,
            TimeSpan duration,
            DailyWorkSchedule.DaySliceSet sliceSet,
            bool checkWorkingDayLimits = true)
        {
            long remainingTicks = duration.Ticks;
            DateTime currentDay = startDateTime.Date;
            TimeSpan currentTod = startDateTime.TimeOfDay;

            if (checkWorkingDayLimits)
            {
                EnsureCanMoveTimeInDirection(currentDay, direction: 1, sliceSet.MaxSpilloverDays);
            }

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay, sliceSet);
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

                if (checkWorkingDayLimits)
                {
                    if (hadSliceAfterCursor)
                    {
                        emptyDays = 0;
                    }
                    else
                    {
                        emptyDays++;
                        CheckTimeSearchLimit(currentDay, direction: 1, emptyDays, sliceSet.MaxSpilloverDays);
                    }
                }

                if (currentDay >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration), "計算結果が DateTime.MaxValue を超えました。");
                }

                currentDay = currentDay.AddDays(1);
                currentTod = TimeSpan.Zero;
            }
        }

        private DateTime SubtractWorkingTimeCore(
            DateTime endDateTime,
            TimeSpan duration,
            DailyWorkSchedule.DaySliceSet sliceSet,
            bool checkWorkingDayLimits = true,
            DateTime? minBoundaryDateTime = null)
        {
            long remainingTicks = duration.Ticks;
            DateTime currentDay = endDateTime.Date;
            TimeSpan currentTod = endDateTime.TimeOfDay;

            if (checkWorkingDayLimits)
            {
                EnsureCanMoveTimeInDirection(currentDay, direction: -1, sliceSet.MaxSpilloverDays);
            }

            int emptyDays = 0;
            while (true)
            {
                if (minBoundaryDateTime.HasValue && currentDay < minBoundaryDateTime.Value.Date)
                {
                    throw new InvalidOperationException("指定日時以前に十分な稼働時間が存在しないため、計算を完了できません。");
                }

                WorkTimeRange[] slices = GetSlicesForDate(currentDay, sliceSet);
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
                        DateTime candidate = DateTime.SpecifyKind(currentDay.Add(resultOffset), endDateTime.Kind);
                        if (minBoundaryDateTime.HasValue && candidate < minBoundaryDateTime.Value)
                        {
                            throw new InvalidOperationException("指定日時以前に十分な稼働時間が存在しないため、計算を完了できません。");
                        }

                        return candidate;
                    }

                    remainingTicks -= availableTicks;
                }

                if (checkWorkingDayLimits)
                {
                    if (hadSliceBeforeCursor)
                    {
                        emptyDays = 0;
                    }
                    else
                    {
                        emptyDays++;
                        CheckTimeSearchLimit(currentDay, direction: -1, emptyDays, sliceSet.MaxSpilloverDays);
                    }
                }

                if (currentDay <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(duration), "計算結果が DateTime.MinValue を下回りました。");
                }

                currentDay = currentDay.AddDays(-1);
                currentTod = TimeFormatHelper.OneDay;
            }
        }

        private TimeSpan GetWorkingTimeCore(
            DateTime startDateTime,
            DateTime endDateTime,
            DailyWorkSchedule.DaySliceSet sliceSet)
        {
            DateTime startDay = startDateTime.Date;
            DateTime endDay = endDateTime.Date;
            long totalTicks = 0;

            for (DateTime day = startDay; ; day = day.AddDays(1))
            {
                TimeSpan windowStart = day == startDay ? startDateTime.TimeOfDay : TimeSpan.Zero;
                TimeSpan windowEnd = day == endDay ? endDateTime.TimeOfDay : TimeFormatHelper.OneDay;

                if (windowEnd > windowStart)
                {
                    WorkTimeRange[] slices = GetSlicesForDate(day, sliceSet);
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

        private DateTime GetNextWorkingTimeCore(
            DateTime dateTime,
            DailyWorkSchedule.DaySliceSet sliceSet,
            bool checkWorkingDayLimits = true)
        {
            if (IsWorkingTimeCore(dateTime, sliceSet))
            {
                return dateTime;
            }

            DateTime currentDay = dateTime.Date;
            TimeSpan currentTod = dateTime.TimeOfDay;

            if (checkWorkingDayLimits)
            {
                EnsureCanMoveTimeInDirection(currentDay, direction: 1, sliceSet.MaxSpilloverDays);
            }

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay, sliceSet);
                for (int i = 0; i < slices.Length; i++)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.Start >= currentTod)
                    {
                        return DateTime.SpecifyKind(currentDay.Add(slice.Start), dateTime.Kind);
                    }
                }

                if (checkWorkingDayLimits)
                {
                    emptyDays++;
                    CheckTimeSearchLimit(currentDay, direction: 1, emptyDays, sliceSet.MaxSpilloverDays);
                }

                if (currentDay >= MaxSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(dateTime), "探索範囲が DateTime.MaxValue を超えました。");
                }

                currentDay = currentDay.AddDays(1);
                currentTod = TimeSpan.Zero;
            }
        }

        private DateTime GetPreviousWorkingTimeCore(
            DateTime dateTime,
            DailyWorkSchedule.DaySliceSet sliceSet,
            bool checkWorkingDayLimits = true)
        {
            if (IsWorkingTimeCore(dateTime, sliceSet))
            {
                return dateTime;
            }

            DateTime currentDay = dateTime.Date;
            TimeSpan currentTod = dateTime.TimeOfDay;

            if (checkWorkingDayLimits)
            {
                EnsureCanMoveTimeInDirection(currentDay, direction: -1, sliceSet.MaxSpilloverDays);
            }

            int emptyDays = 0;
            while (true)
            {
                WorkTimeRange[] slices = GetSlicesForDate(currentDay, sliceSet);
                for (int i = slices.Length - 1; i >= 0; i--)
                {
                    WorkTimeRange slice = slices[i];
                    if (slice.Start < currentTod)
                    {
                        TimeSpan candidateTod = slice.End < currentTod ? slice.End : currentTod;
                        return DateTime.SpecifyKind(currentDay.Add(candidateTod), dateTime.Kind);
                    }
                }

                if (checkWorkingDayLimits)
                {
                    emptyDays++;
                    CheckTimeSearchLimit(currentDay, direction: -1, emptyDays, sliceSet.MaxSpilloverDays);
                }

                if (currentDay <= MinSearchDate)
                {
                    throw new ArgumentOutOfRangeException(nameof(dateTime), "探索範囲が DateTime.MinValue を下回りました。");
                }

                currentDay = currentDay.AddDays(-1);
                currentTod = TimeFormatHelper.OneDay;
            }
        }

        private IReadOnlyList<DateTimeInterval> GetWorkingIntervalsCore(
            DateTime startDateTime,
            DateTime endDateTime,
            DailyWorkSchedule.DaySliceSet sliceSet)
        {
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
                    WorkTimeRange[] slices = GetSlicesForDate(day, sliceSet);
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

        private WorkTimeRange[] GetSlicesForDate(DateTime date, DailyWorkSchedule.DaySliceSet sliceSet)
        {
            return sliceSet.GetDaySlices(date, MinSearchDate, IsWorkingDay);
        }

        private static List<WorkTimeRange>? ParseRangeList(
            List<TimeRangeDefinition>? items,
            string nullElementErrorMessage,
            string paramName)
        {
            if (items == null)
            {
                return null;
            }

            var result = new List<WorkTimeRange>(items.Count);
            foreach (TimeRangeDefinition item in items)
            {
                if (item == null)
                {
                    throw new ArgumentException(nullElementErrorMessage, paramName);
                }

                TimeSpan start = TimeFormatHelper.ParseShiftTime(item.Start);
                TimeSpan end = TimeFormatHelper.ParseShiftTime(item.End);
                result.Add(WorkTimeRange.FromClock(start, end));
            }

            return result;
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

        private DateTime GetEffectiveMaxDate(DateTime maxWorkingDate, int maxSpilloverDays)
        {
            if (maxSpilloverDays <= 0)
            {
                return maxWorkingDate;
            }

            int remaining = (MaxSearchDate - maxWorkingDate).Days;
            return maxSpilloverDays >= remaining
                ? MaxSearchDate
                : maxWorkingDate.AddDays(maxSpilloverDays);
        }

        private void EnsureCanMoveTimeInDirection(DateTime startDay, int direction, int maxSpilloverDays)
        {
            if (!_allSevenDaysNonWorking)
            {
                return;
            }

            if (!_maxWorkingOverrideDate.HasValue || !_minWorkingOverrideDate.HasValue)
            {
                throw new InvalidOperationException("すべての曜日が非稼働に設定されており、稼働日の上書きもないため稼働時間を特定できません。");
            }

            DateTime effectiveMax = GetEffectiveMaxDate(_maxWorkingOverrideDate.Value, maxSpilloverDays);

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

        private void CheckTimeSearchLimit(DateTime currentDay, int direction, int consecutiveEmptyDays, int maxSpilloverDays)
        {
            if (_allSevenDaysNonWorking)
            {
                DateTime effectiveMax = _maxWorkingOverrideDate.HasValue
                    ? GetEffectiveMaxDate(_maxWorkingOverrideDate.Value, maxSpilloverDays)
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
