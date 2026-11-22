using System;
using System.Collections.Generic;
using DateCalc.Holidays;
using DateCalc.Models;

namespace DateCalc
{
    /// <summary>
    /// <see cref="WorkCalendar"/> を直感的かつ流暢（Fluent）に構築するためのビルダーです。
    /// 既定値は「土日非稼働」「日本の国民の祝日（オフライン計算）使用」「08:00-17:00（休憩 12:00-13:00）」です。
    /// </summary>
    public sealed class WorkCalendarBuilder
    {
        private readonly List<WorkTimeRange> _workingPeriods = new List<WorkTimeRange>();
        private readonly List<WorkTimeRange> _breaks = new List<WorkTimeRange>();
        private readonly HashSet<DayOfWeek> _nonWorkingDaysOfWeek = new HashSet<DayOfWeek>
        {
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };
        private readonly Dictionary<MonthDay, string?> _recurringNonWorkingDays = new Dictionary<MonthDay, string?>();
        private readonly Dictionary<DateTime, DateOverride> _dateOverrides = new Dictionary<DateTime, DateOverride>();
        private readonly Dictionary<DateTime, HolidayInfo> _customHolidays = new Dictionary<DateTime, HolidayInfo>();

        private string? _id;
        private string? _name;
        private DailyWorkSchedule? _explicitSchedule;
        private IHolidayProvider _baseHolidayProvider = JapanHolidayProvider.Instance;
        private bool _useJapanHolidays = true;

        /// <summary>
        /// カレンダー識別子を設定します。
        /// </summary>
        /// <param name="id">カレンダー識別子。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder WithId(string? id)
        {
            _id = id;
            return this;
        }

        /// <summary>
        /// カレンダーの表示名を設定します。
        /// </summary>
        /// <param name="name">表示名。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder WithName(string? name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// 作成済みの <see cref="DailyWorkSchedule"/> を直接設定します。
        /// これまで個別に設定した稼働時間帯や休憩はクリアされます。
        /// </summary>
        /// <param name="schedule">稼働スケジュール。</param>
        /// <returns>このビルダーインスタンス。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="schedule"/> が <c>null</c> の場合にスローされます。</exception>
        public WorkCalendarBuilder WithSchedule(DailyWorkSchedule schedule)
        {
            _explicitSchedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            _workingPeriods.Clear();
            _breaks.Clear();
            return this;
        }

        /// <summary>
        /// 既存の稼働時間帯を置き換えて、1 つの稼働時間帯を設定します。
        /// 終了時刻が開始時刻より小さい場合は自動的に日跨ぎ（夜勤）として解釈されます。
        /// </summary>
        /// <param name="start">開始時刻。</param>
        /// <param name="end">終了時刻。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder WithWorkingHours(TimeSpan start, TimeSpan end)
        {
            _explicitSchedule = null;
            _workingPeriods.Clear();
            _workingPeriods.Add(WorkTimeRange.FromClock(start, end));
            return this;
        }

        /// <summary>
        /// 既存の稼働時間帯を保持したまま、稼働時間帯（複数シフト等）を追加します。
        /// </summary>
        /// <param name="start">開始時刻。</param>
        /// <param name="end">終了時刻。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddWorkingHours(TimeSpan start, TimeSpan end)
        {
            _explicitSchedule = null;
            _workingPeriods.Add(WorkTimeRange.FromClock(start, end));
            return this;
        }

        /// <summary>
        /// 稼働時間帯を追加します。
        /// </summary>
        /// <param name="period">稼働時間帯。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddWorkingPeriod(WorkTimeRange period)
        {
            _explicitSchedule = null;
            _workingPeriods.Add(period);
            return this;
        }

        /// <summary>
        /// 休憩時間帯を追加します。終了時刻が開始時刻より小さい場合は日跨ぎ休憩として解釈されます。
        /// </summary>
        /// <param name="start">休憩開始時刻。</param>
        /// <param name="end">休憩終了時刻。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddBreak(TimeSpan start, TimeSpan end)
        {
            _explicitSchedule = null;
            _breaks.Add(WorkTimeRange.FromClock(start, end));
            return this;
        }

        /// <summary>
        /// 休憩時間帯を追加します。
        /// </summary>
        /// <param name="breakRange">休憩時間帯。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddBreak(WorkTimeRange breakRange)
        {
            _explicitSchedule = null;
            _breaks.Add(breakRange);
            return this;
        }

        /// <summary>
        /// 登録されている休憩時間帯をすべてクリアします。
        /// </summary>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder ClearBreaks()
        {
            _explicitSchedule = null;
            _breaks.Clear();
            return this;
        }

        /// <summary>
        /// 定休曜日を指定の曜日に置き換えます（空配列で定休曜日なし）。
        /// </summary>
        /// <param name="daysOfWeek">定休曜日の一覧。</param>
        /// <returns>このビルダーインスタンス。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="daysOfWeek"/> が <c>null</c> の場合にスローされます。</exception>
        public WorkCalendarBuilder WithNonWorkingDaysOfWeek(params DayOfWeek[] daysOfWeek)
        {
            if (daysOfWeek == null)
            {
                throw new ArgumentNullException(nameof(daysOfWeek));
            }

            _nonWorkingDaysOfWeek.Clear();
            foreach (DayOfWeek day in daysOfWeek)
            {
                _nonWorkingDaysOfWeek.Add(day);
            }

            return this;
        }

        /// <summary>
        /// 定休曜日を指定のコレクションに置き換えます。
        /// </summary>
        /// <param name="daysOfWeek">定休曜日のコレクション。</param>
        /// <returns>このビルダーインスタンス。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="daysOfWeek"/> が <c>null</c> の場合にスローされます。</exception>
        public WorkCalendarBuilder WithNonWorkingDaysOfWeek(IEnumerable<DayOfWeek> daysOfWeek)
        {
            if (daysOfWeek == null)
            {
                throw new ArgumentNullException(nameof(daysOfWeek));
            }

            _nonWorkingDaysOfWeek.Clear();
            foreach (DayOfWeek day in daysOfWeek)
            {
                _nonWorkingDaysOfWeek.Add(day);
            }

            return this;
        }

        /// <summary>
        /// 祝日判定に使用する <see cref="IHolidayProvider"/> を指定します。
        /// </summary>
        /// <param name="holidayProvider">祝日プロバイダ。</param>
        /// <returns>このビルダーインスタンス。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="holidayProvider"/> が <c>null</c> の場合にスローされます。</exception>
        public WorkCalendarBuilder WithHolidayProvider(IHolidayProvider holidayProvider)
        {
            _baseHolidayProvider = holidayProvider ?? throw new ArgumentNullException(nameof(holidayProvider));
            _useJapanHolidays = holidayProvider is JapanHolidayProvider;
            return this;
        }

        /// <summary>
        /// 祝日判定を無効化します（カスタム祝日を除く）。
        /// </summary>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder WithoutHolidays()
        {
            _baseHolidayProvider = NullHolidayProvider.Instance;
            _useJapanHolidays = false;
            return this;
        }

        /// <summary>
        /// 内蔵の日本の祝日計算エンジン（<see cref="JapanHolidayProvider"/>）を使用するように設定します。
        /// </summary>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder WithJapanHolidays()
        {
            _baseHolidayProvider = JapanHolidayProvider.Instance;
            _useJapanHolidays = true;
            return this;
        }

        /// <summary>
        /// 内閣府 CSV 形式のローカル祝日データを使用するように設定します。
        /// </summary>
        /// <param name="csvProvider">内閣府 CSV プロバイダ。</param>
        /// <param name="fallbackToJapanHolidays">CSV の収録年範囲外の年を内蔵 <see cref="JapanHolidayProvider"/> へフォールバックする場合は <c>true</c>。</param>
        /// <returns>このビルダーインスタンス。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="csvProvider"/> が <c>null</c> の場合にスローされます。</exception>
        public WorkCalendarBuilder WithCabinetOfficeCsv(
            CabinetOfficeCsvHolidayProvider csvProvider,
            bool fallbackToJapanHolidays = true)
        {
            if (csvProvider == null)
            {
                throw new ArgumentNullException(nameof(csvProvider));
            }

            _baseHolidayProvider = fallbackToJapanHolidays
                ? (IHolidayProvider)CompositeHolidayProvider.WithYearRangeFallback(csvProvider, JapanHolidayProvider.Instance)
                : csvProvider;
            _useJapanHolidays = fallbackToJapanHolidays;
            return this;
        }

        /// <summary>
        /// 特定の日付に独自の祝日を追加します（会社指定の国民的休業日等）。
        /// </summary>
        /// <param name="date">祝日の日付。</param>
        /// <param name="name">祝日名。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddCustomHoliday(DateTime date, string name)
        {
            DateTime key = date.Date;
            _customHolidays[key] = new HolidayInfo(key, name, HolidayKind.Custom);
            return this;
        }

        /// <summary>
        /// 指定した日付を強制的に「稼働日」として上書き登録します（休日出勤・祝日稼働等）。
        /// 同一日付への既存上書きは上書き更新されます。
        /// </summary>
        /// <param name="date">対象日付。</param>
        /// <param name="reason">理由メモ（任意）。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddWorkingDayOverride(DateTime date, string? reason = null)
        {
            DateTime key = date.Date;
            _dateOverrides[key] = new DateOverride(key, isWorkingDay: true, reason);
            return this;
        }

        /// <summary>
        /// 指定した日付を強制的に「非稼働日」として上書き登録します（夏季休暇・臨時休業等）。
        /// 同一日付への既存上書きは上書き更新されます。
        /// </summary>
        /// <param name="date">対象日付。</param>
        /// <param name="reason">理由メモ（任意）。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddNonWorkingDayOverride(DateTime date, string? reason = null)
        {
            DateTime key = date.Date;
            _dateOverrides[key] = new DateOverride(key, isWorkingDay: false, reason);
            return this;
        }

        /// <summary>
        /// 毎年固定の非稼働日（例: 12/29〜1/3、創立記念日）を追加します。
        /// </summary>
        /// <param name="month">月（1〜12）。</param>
        /// <param name="day">日（1〜31）。</param>
        /// <param name="reason">理由・休日名（任意）。</param>
        /// <returns>このビルダーインスタンス。</returns>
        public WorkCalendarBuilder AddRecurringNonWorkingDay(int month, int day, string? reason = null)
        {
            var key = new MonthDay(month, day);
            _recurringNonWorkingDays[key] = reason;
            return this;
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// 既存の稼働時間帯を置き換えて、<see cref="TimeOnly"/> で稼働時間帯を設定します。
        /// </summary>
        public WorkCalendarBuilder WithWorkingHours(TimeOnly start, TimeOnly end)
        {
            return WithWorkingHours(start.ToTimeSpan(), end.ToTimeSpan());
        }

        /// <summary>
        /// <see cref="TimeOnly"/> で稼働時間帯を追加します。
        /// </summary>
        public WorkCalendarBuilder AddWorkingHours(TimeOnly start, TimeOnly end)
        {
            return AddWorkingHours(start.ToTimeSpan(), end.ToTimeSpan());
        }

        /// <summary>
        /// <see cref="TimeOnly"/> で休憩時間帯を追加します。
        /// </summary>
        public WorkCalendarBuilder AddBreak(TimeOnly start, TimeOnly end)
        {
            return AddBreak(start.ToTimeSpan(), end.ToTimeSpan());
        }

        /// <summary>
        /// <see cref="DateOnly"/> で独自の祝日を追加します。
        /// </summary>
        public WorkCalendarBuilder AddCustomHoliday(DateOnly date, string name)
        {
            return AddCustomHoliday(date.ToDateTime(TimeOnly.MinValue), name);
        }

        /// <summary>
        /// <see cref="DateOnly"/> で指定日を稼働日に上書き登録します。
        /// </summary>
        public WorkCalendarBuilder AddWorkingDayOverride(DateOnly date, string? reason = null)
        {
            return AddWorkingDayOverride(date.ToDateTime(TimeOnly.MinValue), reason);
        }

        /// <summary>
        /// <see cref="DateOnly"/> で指定日を非稼働日に上書き登録します。
        /// </summary>
        public WorkCalendarBuilder AddNonWorkingDayOverride(DateOnly date, string? reason = null)
        {
            return AddNonWorkingDayOverride(date.ToDateTime(TimeOnly.MinValue), reason);
        }
#endif

        /// <summary>
        /// 設定内容に基づいてイミュータブルな <see cref="WorkCalendar"/> を生成します。
        /// </summary>
        /// <returns>構築された <see cref="WorkCalendar"/>。</returns>
        public WorkCalendar Build()
        {
            DailyWorkSchedule schedule;
            if (_explicitSchedule != null)
            {
                schedule = _explicitSchedule;
            }
            else if (_workingPeriods.Count == 0 && _breaks.Count == 0)
            {
                schedule = DailyWorkSchedule.StandardDayShift;
            }
            else
            {
                List<WorkTimeRange> periods = _workingPeriods.Count > 0
                    ? _workingPeriods
                    : new List<WorkTimeRange> { new WorkTimeRange(TimeSpan.FromHours(8), TimeSpan.FromHours(17)) };

                schedule = new DailyWorkSchedule(periods, _breaks);
            }

            IHolidayProvider holidayProvider = _baseHolidayProvider;
            if (_customHolidays.Count > 0)
            {
                var customProvider = new InMemoryHolidayProvider(_customHolidays.Values);
                holidayProvider = _baseHolidayProvider is NullHolidayProvider
                    ? customProvider
                    : new CompositeHolidayProvider(customProvider, _baseHolidayProvider);
            }

            return new WorkCalendar(
                _id,
                _name,
                schedule,
                _nonWorkingDaysOfWeek,
                holidayProvider,
                _useJapanHolidays,
                _recurringNonWorkingDays,
                _dateOverrides,
                _customHolidays.Values);
        }
    }
}
