using System;
using System.Collections.Generic;
using WorkTimeCalc.Models;
using WorkTimeCalc.Serialization;
using Xunit;

namespace WorkTimeCalc.Tests
{
    public class OvertimeTests
    {
        [Fact]
        public void DailyWorkSchedule_WithOvertimeAndSharedBreaks_NormalizesCorrectly()
        {
            var schedule = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("08:00-17:00") },
                new[]
                {
                    WorkTimeRange.Parse("12:00-13:00"),
                    WorkTimeRange.Parse("17:00-17:15")
                },
                new[] { WorkTimeRange.Parse("17:00-19:15") });

            Assert.True(schedule.HasOvertime);
            Assert.Equal(TimeSpan.FromHours(8), schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(2), schedule.TotalOvertimeDuration);
            Assert.Equal(TimeSpan.FromHours(10), schedule.TotalWorkingDurationWithOvertime);
            Assert.False(schedule.HasOvernightSpillover);
            Assert.False(schedule.HasOvernightSpilloverWithOvertime);

            Assert.Single(schedule.EffectiveOvertimeRanges);
            Assert.Equal(WorkTimeRange.Parse("17:15-19:15"), schedule.EffectiveOvertimeRanges[0]);

            Assert.Equal(3, schedule.EffectiveTotalWorkingRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("08:00-12:00"), schedule.EffectiveTotalWorkingRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("13:00-17:00"), schedule.EffectiveTotalWorkingRanges[1]);
            Assert.Equal(WorkTimeRange.Parse("17:15-19:15"), schedule.EffectiveTotalWorkingRanges[2]);
        }

        [Fact]
        public void DailyWorkSchedule_OvertimeOverlappingRegular_PrioritizesRegularWithoutDoubleCount()
        {
            var schedule = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("08:00-17:00") },
                new[] { WorkTimeRange.Parse("12:00-13:00") },
                new[]
                {
                    WorkTimeRange.Parse("07:00-09:00"),
                    WorkTimeRange.Parse("16:00-19:00")
                });

            Assert.Equal(TimeSpan.FromHours(8), schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(3), schedule.TotalOvertimeDuration);
            Assert.Equal(TimeSpan.FromHours(11), schedule.TotalWorkingDurationWithOvertime);
            Assert.Equal(2, schedule.EffectiveOvertimeRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("07:00-08:00"), schedule.EffectiveOvertimeRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("17:00-19:00"), schedule.EffectiveOvertimeRanges[1]);
        }

        [Fact]
        public void DailyWorkSchedule_OvertimeCoveredByRegularOrBreaks_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("08:00-17:00") },
                new[] { WorkTimeRange.Parse("12:00-13:00"), WorkTimeRange.Parse("17:00-18:00") },
                new[] { WorkTimeRange.Parse("10:00-18:00") }));
        }

        [Fact]
        public void DailyWorkSchedule_OvernightOvertimeOnDayAndNightShift_NormalizesSpillover()
        {
            // 日勤 + 深夜帯に跨ぐ残業 (17:00-25:00)
            var dayWithOvernightOt = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("08:00-17:00") },
                new[] { WorkTimeRange.Parse("12:00-13:00") },
                new[] { WorkTimeRange.Parse("17:00-25:00") });

            Assert.False(dayWithOvernightOt.HasOvernightSpillover);
            Assert.True(dayWithOvernightOt.HasOvernightSpilloverWithOvertime);
            Assert.Equal(TimeSpan.FromHours(8), dayWithOvernightOt.TotalOvertimeDuration);

            // 夜勤 22:00-06:00（深夜休憩 02:00-03:00）＋ 翌朝残業 06:00-08:00（30:00-32:00）
            var nightWithOt = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("22:00-06:00") },
                new[] { WorkTimeRange.Parse("02:00-03:00") },
                new[] { WorkTimeRange.Parse("30:00-32:00") });

            Assert.True(nightWithOt.HasOvertime);
            Assert.True(nightWithOt.HasOvernightSpillover);
            Assert.True(nightWithOt.HasOvernightSpilloverWithOvertime);
            Assert.Equal(TimeSpan.FromHours(7), nightWithOt.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(2), nightWithOt.TotalOvertimeDuration);
            Assert.Equal(WorkTimeRange.Parse("30:00-32:00"), nightWithOt.EffectiveOvertimeRanges[0]);
        }

        [Fact]
        public void WorkCalendarBuilder_AddOvertimeHoursOnDefault_PreservesStandardDayShiftAndLunchBreak()
        {
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .AddOvertimeHours(TimeSpan.FromHours(17.25), TimeSpan.FromHours(19.25))
                .Build();

            Assert.Equal(TimeSpan.FromHours(8), cal.Schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(2), cal.Schedule.TotalOvertimeDuration);
            Assert.Equal(TimeSpan.FromHours(10), cal.Schedule.TotalWorkingDurationWithOvertime);

            // ClearOvertime で残業枠を消しても動作すること
            WorkCalendar cleared = WorkCalendar.CreateBuilder()
                .AddOvertimeHours(TimeOnly.Parse("17:15"), TimeOnly.Parse("19:15"))
                .ClearOvertime()
                .Build();
            Assert.False(cleared.Schedule.HasOvertime);
            Assert.Equal(TimeSpan.FromHours(8), cleared.Schedule.TotalWorkingDuration);
        }

        [Fact]
        public void OvertimePolicy_MaxPerDay_ValidationAndCalculationsOnRegularCalendar()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OvertimePolicy.MaxPerDay(TimeSpan.FromMinutes(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => OvertimePolicy.MaxPerDay(TimeSpan.FromHours(25)));
            Assert.Equal(OvertimePolicy.None, OvertimePolicy.MaxPerDay(TimeSpan.Zero));

            // 定時のみ（08:00-17:00、昼休憩 12:00-13:00）の標準カレンダー
            WorkCalendar cal = WorkCalendar.CreateDefault();
            var monStart = new DateTime(2026, 5, 11, 8, 0, 0);
            OvertimePolicy max2h = OvertimePolicy.MaxPerDay(TimeSpan.FromHours(2));

            // 既定（定時のみ）は翌日 10:00
            Assert.Equal(new DateTime(2026, 5, 12, 10, 0, 0), cal.AddWorkingTime(monStart, TimeSpan.FromHours(10)));

            // MaxPerDay(2h) で 10H → 当日 19:00
            DateTime end10h = cal.AddWorkingTime(monStart, TimeSpan.FromHours(10), max2h);
            Assert.Equal(new DateTime(2026, 5, 11, 19, 0, 0), end10h);
            Assert.Equal(monStart, cal.SubtractWorkingTime(end10h, TimeSpan.FromHours(10), max2h));

            // MaxPerDay(2h) で 11H → 当日 10H + 翌日 09:00
            DateTime end11h = cal.AddWorkingTime(monStart, TimeSpan.FromHours(11), max2h);
            Assert.Equal(new DateTime(2026, 5, 12, 9, 0, 0), end11h);
            Assert.Equal(monStart, cal.SubtractWorkingTime(end11h, TimeSpan.FromHours(11), max2h));

            // 内訳: 08:00〜19:00 -> 定時 8h, 残業 2h, 合計 10h
            WorkingTimeBreakdown breakdown = cal.GetWorkingTimeBreakdown(monStart, end10h, max2h);
            Assert.Equal(TimeSpan.FromHours(8), breakdown.Regular);
            Assert.Equal(TimeSpan.FromHours(2), breakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(10), breakdown.Total);

            // 稼働日カウントは定時ベースで変わらない
            Assert.Equal(5, cal.CountWorkingDays(new DateTime(2026, 5, 11), new DateTime(2026, 5, 15)));
            Assert.Equal(new DateTime(2026, 5, 12), cal.AddWorkingDays(new DateTime(2026, 5, 11), 1));
        }

        [Fact]
        public void OvertimePolicy_MaxPerDay_RespectsPreOvertimeBreakAndOvernightSpillover()
        {
            // 残業前休憩 17:00-17:15 を持つ定時カレンダー（残業枠は未設定）
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .AddBreak(TimeSpan.FromHours(12), TimeSpan.FromHours(13))
                .AddBreak(new TimeSpan(17, 0, 0), new TimeSpan(17, 15, 0))
                .Build();

            var monStart = new DateTime(2026, 5, 11, 8, 0, 0);
            OvertimePolicy max2h = OvertimePolicy.MaxPerDay(TimeSpan.FromHours(2));

            // 8h ちょうどは 17:00、8h1m は 17:16、10h は 19:15
            Assert.Equal(new DateTime(2026, 5, 11, 17, 0, 0), cal.AddWorkingTime(monStart, TimeSpan.FromHours(8), max2h));
            Assert.Equal(new DateTime(2026, 5, 11, 17, 16, 0), cal.AddWorkingTime(monStart, new TimeSpan(8, 1, 0), max2h));
            DateTime end10h = cal.AddWorkingTime(monStart, TimeSpan.FromHours(10), max2h);
            Assert.Equal(new DateTime(2026, 5, 11, 19, 15, 0), end10h);
            Assert.Equal(monStart, cal.SubtractWorkingTime(end10h, TimeSpan.FromHours(10), max2h));

            // 金曜から MaxPerDay(8h) で日跨ぎ（17:00-17:15 休憩あり → 17:15 + 8h = 土曜 01:15）
            OvertimePolicy max8h = OvertimePolicy.MaxPerDay(TimeSpan.FromHours(8));
            var friStart = new DateTime(2026, 5, 8, 8, 0, 0);
            DateTime satFinish = cal.AddWorkingTime(friStart, TimeSpan.FromHours(16), max8h);
            Assert.Equal(new DateTime(2026, 5, 9, 1, 15, 0), satFinish);
            Assert.True(cal.IsWorkingTime(new DateTime(2026, 5, 9, 1, 0, 0), max8h));
            Assert.False(cal.IsWorkingTime(new DateTime(2026, 5, 9, 1, 15, 0), max8h));

            WorkingTimeBreakdown friBreakdown = cal.GetWorkingTimeBreakdown(friStart, satFinish, max8h);
            Assert.Equal(TimeSpan.FromHours(8), friBreakdown.Regular);
            Assert.Equal(TimeSpan.FromHours(8), friBreakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(16), friBreakdown.Total);
        }

        [Fact]
        public void OvertimePolicy_MaxPerDay_OnNightShift_ExtendsAfterShiftEnd()
        {
            WorkCalendar night = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(22), TimeSpan.FromHours(6))
                .AddBreak(TimeSpan.FromHours(2), TimeSpan.FromHours(3))
                .Build();

            OvertimePolicy max2h = OvertimePolicy.MaxPerDay(TimeSpan.FromHours(2));
            var friNight = new DateTime(2026, 5, 8, 22, 0, 0);

            // 夜勤 7h + 残業 2h = 9h → 土曜 08:00
            DateTime satMorning = night.AddWorkingTime(friNight, TimeSpan.FromHours(9), max2h);
            Assert.Equal(new DateTime(2026, 5, 9, 8, 0, 0), satMorning);
            Assert.Equal(friNight, night.SubtractWorkingTime(satMorning, TimeSpan.FromHours(9), max2h));
        }

        [Fact]
        public void OvertimePolicy_Unlimited_ExtendsContinuouslyWithBreaksAndWeekendRollover()
        {
            WorkCalendar cal = WorkCalendar.CreateDefault(); // 08:00-17:00, 昼休憩 12:00-13:00
            var monStart = new DateTime(2026, 5, 11, 8, 0, 0);

            // 10h → 当日 19:00
            DateTime end10h = cal.AddWorkingTime(monStart, TimeSpan.FromHours(10), OvertimePolicy.Unlimited);
            Assert.Equal(new DateTime(2026, 5, 11, 19, 0, 0), end10h);

            // 15h → 08:00-12:00(4h) + 13:00-24:00(11h) = 翌日 00:00
            DateTime end15h = cal.AddWorkingTime(monStart, TimeSpan.FromHours(15), OvertimePolicy.Unlimited);
            Assert.Equal(new DateTime(2026, 5, 12, 0, 0, 0), end15h);
            Assert.Equal(monStart, cal.SubtractWorkingTime(end15h, TimeSpan.FromHours(15), OvertimePolicy.Unlimited));

            // 非稼働日・始業前開始時は次の定時開始時刻へスナップしてから延長
            var satMorning = new DateTime(2026, 5, 9, 8, 0, 0);
            Assert.Equal(
                new DateTime(2026, 5, 11, 19, 0, 0),
                cal.AddWorkingTime(satMorning, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));

            var monEarly = new DateTime(2026, 5, 11, 6, 0, 0);
            Assert.Equal(
                new DateTime(2026, 5, 11, 19, 0, 0),
                cal.AddWorkingTime(monEarly, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));

            // 金曜 08:00 + 20h → 金曜(4+11=15h) + 土曜[00-05](5h) = 土曜 05:00
            var friStart = new DateTime(2026, 5, 8, 8, 0, 0);
            DateTime satEnd = cal.AddWorkingTime(friStart, TimeSpan.FromHours(20), OvertimePolicy.Unlimited);
            Assert.Equal(new DateTime(2026, 5, 9, 5, 0, 0), satEnd);
            Assert.Equal(friStart, cal.SubtractWorkingTime(satEnd, TimeSpan.FromHours(20), OvertimePolicy.Unlimited));

            // 内訳確認（通常8h + 残業12h = 20h）および逆方向負数確認
            WorkingTimeBreakdown breakdown = cal.GetWorkingTimeBreakdown(friStart, satEnd, OvertimePolicy.Unlimited);
            Assert.Equal(TimeSpan.FromHours(8), breakdown.Regular);
            Assert.Equal(TimeSpan.FromHours(12), breakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(20), breakdown.Total);

            WorkingTimeBreakdown reversed = cal.GetWorkingTimeBreakdown(satEnd, friStart, OvertimePolicy.Unlimited);
            Assert.Equal(breakdown.Negate(), reversed);

            // GetShiftIntervals は Unlimited で ArgumentException
            Assert.Throws<ArgumentException>(() => cal.GetShiftIntervals(new DateTime(2026, 5, 11), OvertimePolicy.Unlimited));
            Assert.Throws<ArgumentException>(() => cal.GetShiftIntervals(new DateOnly(2026, 5, 11), OvertimePolicy.Unlimited));
        }

        [Fact]
        public void OvertimePolicy_Unlimited_WithPreOvertimeBreak_SkipsBreak()
        {
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .AddBreak(TimeSpan.FromHours(12), TimeSpan.FromHours(13))
                .AddBreak(new TimeSpan(17, 0, 0), new TimeSpan(17, 15, 0))
                .Build();

            var monStart = new DateTime(2026, 5, 11, 8, 0, 0);
            Assert.Equal(
                new DateTime(2026, 5, 11, 19, 15, 0),
                cal.AddWorkingTime(monStart, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));

            Assert.Equal(
                new DateTime(2026, 5, 11, 19, 15, 0),
                cal.AddWorkingTime(new DateTime(2026, 5, 11, 17, 0, 0), TimeSpan.FromHours(2), OvertimePolicy.Unlimited));
        }

        [Fact]
        public void OvertimePolicy_CalendarDefined_And_IsOvertime_And_HolidayOverrideWorkConsistently()
        {
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .AddBreak(TimeSpan.FromHours(12), TimeSpan.FromHours(13))
                .AddBreak(new TimeSpan(17, 0, 0), new TimeSpan(17, 15, 0))
                .AddOvertimeHours(new TimeSpan(17, 0, 0), new TimeSpan(19, 15, 0))
                .AddWorkingDayOverride(new DateTime(2026, 5, 4), "GW祝日出勤")
                .Build();

            // IsOvertime は事前定義残業帯のみ true
            Assert.False(cal.IsOvertime(new DateTime(2026, 5, 11, 16, 59, 0)));
            Assert.False(cal.IsOvertime(new DateTime(2026, 5, 11, 17, 0, 0))); // 残業前休憩
            Assert.True(cal.IsOvertime(new DateTime(2026, 5, 11, 17, 15, 0)));
            Assert.False(cal.IsOvertime(new DateTime(2026, 5, 11, 19, 15, 0)));

            // 休日出勤上書き日（5/4 祝日）も定時＋残業枠が同じように適用される
            var overrideStart = new DateTime(2026, 5, 4, 8, 0, 0);
            Assert.True(cal.IsOvertime(new DateTime(2026, 5, 4, 18, 0, 0)));
            DateTime overrideFinish = cal.AddWorkingTime(overrideStart, TimeSpan.FromHours(10), OvertimePolicy.CalendarDefined);
            Assert.Equal(new DateTime(2026, 5, 4, 19, 15, 0), overrideFinish);

            // 11h は 5/4 に 10h 消化し、5/5・5/6(振替)をスキップして 5/7 09:00 に完了
            DateTime nextFinish = cal.AddWorkingTime(overrideStart, TimeSpan.FromHours(11), OvertimePolicy.CalendarDefined);
            Assert.Equal(new DateTime(2026, 5, 7, 9, 0, 0), nextFinish);
            Assert.Equal(overrideStart, cal.SubtractWorkingTime(nextFinish, TimeSpan.FromHours(11), OvertimePolicy.CalendarDefined));

            // 既定 GetWorkingTimeBreakdown は CalendarDefined を使用
            WorkingTimeBreakdown breakdown = cal.GetWorkingTimeBreakdown(overrideStart, overrideFinish);
            Assert.Equal(TimeSpan.FromHours(8), breakdown.Regular);
            Assert.Equal(TimeSpan.FromHours(2), breakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(10), breakdown.Total);

            // GetShiftIntervals with CalendarDefined
            IReadOnlyList<DateTimeInterval> shiftWithOt = cal.GetShiftIntervals(new DateOnly(2026, 5, 4), OvertimePolicy.CalendarDefined);
            Assert.Equal(3, shiftWithOt.Count);
            Assert.Equal(new DateTimeInterval(new DateTime(2026, 5, 4, 17, 15, 0), new DateTime(2026, 5, 4, 19, 15, 0)), shiftWithOt[2]);
            Assert.Empty(cal.GetShiftIntervals(new DateOnly(2026, 5, 5), OvertimePolicy.CalendarDefined));

            // 残業枠未定義カレンダーでは CalendarDefined と None が同一・IsOvertime は常に false
            WorkCalendar noOtCal = WorkCalendar.CreateDefault();
            Assert.False(noOtCal.IsOvertime(new DateTime(2026, 5, 11, 18, 0, 0)));
            Assert.Equal(
                noOtCal.AddWorkingTime(new DateTime(2026, 5, 11, 8, 0, 0), TimeSpan.FromHours(10)),
                noOtCal.AddWorkingTime(new DateTime(2026, 5, 11, 8, 0, 0), TimeSpan.FromHours(10), OvertimePolicy.CalendarDefined));
        }

        [Fact]
        public void JsonSerialization_RoundTripsOvertimePeriods_AndOmitsWhenUnconfigured()
        {
            const string jsonWithOt = """
                {
                  "id": "ot-factory",
                  "name": "残業設定あり工場",
                  "workingPeriods": [{ "start": "08:00", "end": "17:00" }],
                  "breaks": [
                    { "start": "12:00", "end": "13:00" },
                    { "start": "17:00", "end": "17:15" }
                  ],
                  "overtimePeriods": [{ "start": "17:00", "end": "19:15" }]
                }
                """;

            WorkCalendar cal = WorkCalendarSerializer.LoadCalendarFromJson(jsonWithOt);
            Assert.True(cal.Schedule.HasOvertime);
            Assert.Equal(TimeSpan.FromHours(2), cal.Schedule.TotalOvertimeDuration);

            string reserialized = WorkCalendarSerializer.Serialize(cal);
            Assert.Contains("overtimePeriods", reserialized);

            WorkCalendar reloaded = WorkCalendarSerializer.LoadCalendarFromJson(reserialized);
            Assert.Equal(cal.Schedule.TotalOvertimeDuration, reloaded.Schedule.TotalOvertimeDuration);
            Assert.Equal(cal.Schedule.EffectiveOvertimeRanges, reloaded.Schedule.EffectiveOvertimeRanges);

            // 残業未設定時は JSON に overtimePeriods を出力しない（後方互換）
            WorkCalendar plain = WorkCalendar.CreateDefault();
            string plainJson = WorkCalendarSerializer.Serialize(plain);
            Assert.DoesNotContain("overtimePeriods", plainJson);
        }
    }
}
