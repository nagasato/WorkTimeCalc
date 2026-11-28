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

        [Fact]
        public void NightShift_BreakNormalizationForRegular_IsUnaffectedByEarlyOvertime()
        {
            // PR指摘#2: 定時 22:00-30:00・休憩 02:00-03:00 に始業日側残業 02:00-04:00 を追加しても
            // 定時の実働は 7h（22:00-26:00, 27:00-30:00）のまま変わらず、残業は 03:00-04:00（1h）となること
            var schedule = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("22:00-30:00") },
                new[] { WorkTimeRange.Parse("02:00-03:00") },
                new[] { WorkTimeRange.Parse("02:00-04:00") });

            Assert.Equal(TimeSpan.FromHours(7), schedule.TotalWorkingDuration);
            Assert.Equal(2, schedule.EffectiveWorkingRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("22:00-26:00"), schedule.EffectiveWorkingRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("27:00-30:00"), schedule.EffectiveWorkingRanges[1]);

            Assert.Equal(TimeSpan.FromHours(1), schedule.TotalOvertimeDuration);
            Assert.Single(schedule.EffectiveOvertimeRanges);
            Assert.Equal(WorkTimeRange.Parse("03:00-04:00"), schedule.EffectiveOvertimeRanges[0]);
        }

        [Fact]
        public void OvertimePolicy_Unlimited_PreservesSplitRegularGapsAndIgnoresNextDayBreaksInsideRegular()
        {
            // PR指摘#3a: 休憩なしの分割定時 [08:00-12:00), [13:00-17:00) では昼ギャップ 12:00-13:00 が維持されること
            WorkCalendar splitCal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(12))
                .AddWorkingHours(TimeSpan.FromHours(13), TimeSpan.FromHours(17))
                .Build();

            var monStart = new DateTime(2026, 5, 11, 8, 0, 0);
            Assert.Equal(new DateTime(2026, 5, 11, 17, 0, 0), splitCal.AddWorkingTime(monStart, TimeSpan.FromHours(8), OvertimePolicy.Unlimited));
            DateTime end10h = splitCal.AddWorkingTime(monStart, TimeSpan.FromHours(10), OvertimePolicy.Unlimited);
            Assert.Equal(new DateTime(2026, 5, 11, 19, 0, 0), end10h);
            Assert.Equal(monStart, splitCal.SubtractWorkingTime(end10h, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));

            WorkingTimeBreakdown splitBreakdown = splitCal.GetWorkingTimeBreakdown(monStart, new DateTime(2026, 5, 11, 17, 0, 0), OvertimePolicy.Unlimited);
            Assert.Equal(TimeSpan.FromHours(8), splitBreakdown.Regular);
            Assert.Equal(TimeSpan.Zero, splitBreakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(8), splitBreakdown.Total);

            // PR指摘#3b: 定時 08:00-17:00 + 翌日休憩 36:00-37:00 では当日定時（9h）が削られず残業が負にならないこと
            WorkCalendar nextDayBreakCal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .AddBreak(WorkTimeRange.Parse("36:00-37:00"))
                .Build();

            Assert.Equal(new DateTime(2026, 5, 11, 17, 0, 0), nextDayBreakCal.AddWorkingTime(monStart, TimeSpan.FromHours(9), OvertimePolicy.Unlimited));
            WorkingTimeBreakdown nextDayBreakBreakdown = nextDayBreakCal.GetWorkingTimeBreakdown(
                monStart,
                new DateTime(2026, 5, 11, 17, 0, 0),
                OvertimePolicy.Unlimited);
            Assert.Equal(TimeSpan.FromHours(9), nextDayBreakBreakdown.Regular);
            Assert.Equal(TimeSpan.Zero, nextDayBreakBreakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(9), nextDayBreakBreakdown.Total);
        }

        [Fact]
        public void OvertimePolicy_MaxPerDay_SupportsExtendingPast48HoursWithoutTruncation()
        {
            // PR指摘#4: 休憩なし夜勤 22:00-30:00 に MaxPerDay(20h) を指定し、金曜 22:00 + 28h = 日曜 02:00（50:00）になること
            WorkCalendar night = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(22), TimeSpan.FromHours(6))
                .Build();

            OvertimePolicy max20h = OvertimePolicy.MaxPerDay(TimeSpan.FromHours(20));
            var friNight = new DateTime(2026, 5, 8, 22, 0, 0);

            DateTime sunMorning = night.AddWorkingTime(friNight, TimeSpan.FromHours(28), max20h);
            Assert.Equal(new DateTime(2026, 5, 10, 2, 0, 0), sunMorning);
            Assert.Equal(friNight, night.SubtractWorkingTime(sunMorning, TimeSpan.FromHours(28), max20h));

            WorkingTimeBreakdown breakdown = night.GetWorkingTimeBreakdown(friNight, sunMorning, max20h);
            Assert.Equal(TimeSpan.FromHours(8), breakdown.Regular);
            Assert.Equal(TimeSpan.FromHours(20), breakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(28), breakdown.Total);

            IReadOnlyList<DateTimeInterval> shift = night.GetShiftIntervals(new DateTime(2026, 5, 8), max20h);
            Assert.Single(shift);
            Assert.Equal(new DateTimeInterval(friNight, sunMorning), shift[0]);
        }

        [Fact]
        public void IsOvertime_ExcludesRegularWorkingHoursWhenPreviousDayOvertimeOverlapsToday()
        {
            // PR指摘#5: 定時 08:00-17:00・残業 17:00-34:00（翌10:00まで）の場合、
            // 翌稼働日の 09:00 は定時優先のため IsOvertime == false、翌稼働日 07:00 や土曜 09:00 は IsOvertime == true となること
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .ClearBreaks()
                .WithOvertimeHours(TimeSpan.FromHours(17), TimeSpan.FromHours(34))
                .Build();

            // 2026-05-12(火) 09:00 は前日(月)残業と重複するが定時内 -> false
            var tue9am = new DateTime(2026, 5, 12, 9, 0, 0);
            Assert.True(cal.IsWorkingTime(tue9am));
            Assert.False(cal.IsOvertime(tue9am));

            // 2026-05-12(火) 07:00 は定時外かつ前日残業内 -> true
            Assert.True(cal.IsOvertime(new DateTime(2026, 5, 12, 7, 0, 0)));

            // 2026-05-09(土) 09:00 は非稼働日かつ前日(金)残業内 -> true
            Assert.True(cal.IsOvertime(new DateTime(2026, 5, 9, 9, 0, 0)));
        }

        [Fact]
        public void OvertimePolicy_Unlimited_GetWorkingTimeAndIntervals_SnapToFirstValidShiftStartInRange()
        {
            // PR指摘#6: 土曜 08:00 〜 月曜 19:00 を Unlimited で集計すると、
            // 土日や月曜始業前を含めず月曜 08:00 開始扱いで 10 時間となること
            WorkCalendar cal = WorkCalendar.CreateDefault();
            var satStart = new DateTime(2026, 5, 9, 8, 0, 0);
            var monFinish = new DateTime(2026, 5, 11, 19, 0, 0);

            Assert.Equal(monFinish, cal.AddWorkingTime(satStart, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));
            Assert.Equal(TimeSpan.FromHours(10), cal.GetWorkingTime(satStart, monFinish, OvertimePolicy.Unlimited));

            IReadOnlyList<DateTimeInterval> intervals = cal.GetWorkingIntervals(satStart, monFinish, OvertimePolicy.Unlimited);
            Assert.Equal(2, intervals.Count);
            Assert.Equal(new DateTimeInterval(new DateTime(2026, 5, 11, 8, 0, 0), new DateTime(2026, 5, 11, 12, 0, 0)), intervals[0]);
            Assert.Equal(new DateTimeInterval(new DateTime(2026, 5, 11, 13, 0, 0), monFinish), intervals[1]);

            WorkingTimeBreakdown breakdown = cal.GetWorkingTimeBreakdown(satStart, monFinish, OvertimePolicy.Unlimited);
            Assert.Equal(TimeSpan.FromHours(8), breakdown.Regular);
            Assert.Equal(TimeSpan.FromHours(2), breakdown.Overtime);
            Assert.Equal(TimeSpan.FromHours(10), breakdown.Total);

            // 有効な開始がない範囲（土日のみ、または月曜 08:00 ちょうどまで）は 0 と空リスト
            Assert.Equal(TimeSpan.Zero, cal.GetWorkingTime(satStart, new DateTime(2026, 5, 10, 17, 0, 0), OvertimePolicy.Unlimited));
            Assert.Empty(cal.GetWorkingIntervals(satStart, new DateTime(2026, 5, 11, 8, 0, 0), OvertimePolicy.Unlimited));

            // IsWorkingTime(Unlimited) と GetNextWorkingTime / GetPreviousWorkingTime の整合性
            Assert.False(cal.IsWorkingTime(satStart, OvertimePolicy.Unlimited));
            Assert.Equal(new DateTime(2026, 5, 11, 8, 0, 0), cal.GetNextWorkingTime(satStart, OvertimePolicy.Unlimited));
            Assert.Equal(new DateTime(2026, 5, 8, 17, 0, 0), cal.GetPreviousWorkingTime(satStart, OvertimePolicy.Unlimited));
        }

        [Fact]
        public void WorkCalendarBuilder_WithScheduleFollowedByOvertimeMethods_PreservesSchedulePeriodsAndBreaks()
        {
            // PR指摘#7: WithSchedule の後に残業系メソッドを呼んでも明示スケジュールの定時・休憩（および既存残業）が保持されること
            var nightWithOt = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("22:00-06:00") },
                new[] { WorkTimeRange.Parse("02:00-03:00") },
                new[] { WorkTimeRange.Parse("06:00-07:00") });

            WorkCalendar replaced = WorkCalendar.CreateBuilder()
                .WithSchedule(nightWithOt)
                .WithOvertimeHours(TimeSpan.FromHours(6), TimeSpan.FromHours(8))
                .Build();
            Assert.Equal(TimeSpan.FromHours(7), replaced.Schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(2), replaced.Schedule.TotalOvertimeDuration);
            Assert.True(replaced.Schedule.HasOvernightSpillover);

            WorkCalendar added = WorkCalendar.CreateBuilder()
                .WithSchedule(nightWithOt)
                .AddOvertimeHours(TimeSpan.FromHours(7), TimeSpan.FromHours(8))
                .Build();
            Assert.Equal(TimeSpan.FromHours(7), added.Schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(2), added.Schedule.TotalOvertimeDuration);

            WorkCalendar addedPeriod = WorkCalendar.CreateBuilder()
                .WithSchedule(nightWithOt)
                .AddOvertimePeriod(WorkTimeRange.Parse("31:00-33:00"))
                .Build();
            Assert.Equal(TimeSpan.FromHours(7), addedPeriod.Schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(3), addedPeriod.Schedule.TotalOvertimeDuration);

            WorkCalendar cleared = WorkCalendar.CreateBuilder()
                .WithSchedule(nightWithOt)
                .ClearOvertime()
                .Build();
            Assert.Equal(TimeSpan.FromHours(7), cleared.Schedule.TotalWorkingDuration);
            Assert.False(cleared.Schedule.HasOvertime);
            Assert.True(cleared.Schedule.HasOvernightSpillover);
        }

        [Fact]
        public void OvertimePolicy_MaxPerDay_FullDayBreak_ThrowsWhenOvertimeCannotBeAllocated()
        {
            // Copilot再レビュー指摘#1: 定時 08:00-17:00・休憩 24:00-48:00 のとき、
            // 休憩前で収まる MaxPerDay(7h) は完了し、終日休憩に突入する MaxPerDay(8h) は例外で終了すること
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .AddBreak(WorkTimeRange.Parse("24:00-48:00"))
                .Build();

            var monStart = new DateTime(2026, 5, 11, 8, 0, 0);
            Assert.Equal(
                new DateTime(2026, 5, 12, 0, 0, 0),
                cal.AddWorkingTime(monStart, TimeSpan.FromHours(16), OvertimePolicy.MaxPerDay(TimeSpan.FromHours(7))));

            Assert.Throws<InvalidOperationException>(() =>
                cal.AddWorkingTime(monStart, TimeSpan.FromHours(17), OvertimePolicy.MaxPerDay(TimeSpan.FromHours(8))));

            // 終日休憩で非稼働日スライスが空となる場合、全曜日非稼働カレンダー＋Unlimited でも探索上限で停止すること
            WorkCalendar allNonWorkingOneOverride = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(8), TimeSpan.FromHours(17))
                .AddBreak(WorkTimeRange.Parse("24:00-48:00"))
                .WithNonWorkingDaysOfWeek(
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday)
                .WithoutHolidays()
                .AddWorkingDayOverride(new DateTime(2026, 5, 11), "単日稼働")
                .Build();

            Assert.Throws<InvalidOperationException>(() =>
                allNonWorkingOneOverride.AddWorkingTime(monStart, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));
        }

        [Fact]
        public void OvertimePolicy_Unlimited_SubtractWorkingTime_ValidatesActiveShiftAnchorAndCalendarBounds()
        {
            // Copilot再レビュー指摘#2: Unlimited の逆算で有効な始業開始日時が存在しない場合は例外となること
            WorkCalendar cal = WorkCalendar.CreateDefault();
            var mon9am = new DateTime(2026, 5, 11, 9, 0, 0);

            Assert.Equal(
                new DateTime(2026, 5, 11, 8, 0, 0),
                cal.SubtractWorkingTime(mon9am, TimeSpan.FromHours(1), OvertimePolicy.Unlimited));

            Assert.Throws<InvalidOperationException>(() =>
                cal.SubtractWorkingTime(mon9am, TimeSpan.FromHours(2), OvertimePolicy.Unlimited));
            Assert.Throws<InvalidOperationException>(() =>
                cal.AddWorkingTime(mon9am, TimeSpan.FromHours(-2), OvertimePolicy.Unlimited));

            // 全曜日非稼働・上書きなしのカレンダーは常に例外
            WorkCalendar noWorkingDaysCal = WorkCalendar.CreateBuilder()
                .WithNonWorkingDaysOfWeek(
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday)
                .WithoutHolidays()
                .Build();

            Assert.Throws<InvalidOperationException>(() =>
                noWorkingDaysCal.SubtractWorkingTime(new DateTime(2026, 5, 11, 19, 0, 0), TimeSpan.FromHours(2), OvertimePolicy.Unlimited));

            // 全曜日非稼働・単日上書きカレンダーは上書き日の始業時刻が下限
            WorkCalendar singleOverrideCal = WorkCalendar.CreateBuilder()
                .WithNonWorkingDaysOfWeek(
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday)
                .WithoutHolidays()
                .AddWorkingDayOverride(new DateTime(2026, 5, 11), "単日稼働")
                .Build();

            var overrideFinish = new DateTime(2026, 5, 11, 19, 0, 0);
            Assert.Equal(
                new DateTime(2026, 5, 11, 8, 0, 0),
                singleOverrideCal.SubtractWorkingTime(overrideFinish, TimeSpan.FromHours(10), OvertimePolicy.Unlimited));
            Assert.Throws<InvalidOperationException>(() =>
                singleOverrideCal.SubtractWorkingTime(overrideFinish, TimeSpan.FromHours(11), OvertimePolicy.Unlimited));
            Assert.Throws<InvalidOperationException>(() =>
                singleOverrideCal.SubtractWorkingTime(new DateTime(2026, 5, 10, 17, 0, 0), TimeSpan.FromHours(1), OvertimePolicy.Unlimited));
        }

        [Fact]
        public void OvertimePolicy_Unlimited_NextDayStartSchedule_AppliesShiftedRegularStartLowerBound()
        {
            // Copilot再レビュー指摘#3: 翌日始業スケジュール (26:00-30:00) では前日稼働日でも翌日の 02:00 未満は始業済みとみなさないこと
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithSchedule(new DailyWorkSchedule(new[] { WorkTimeRange.Parse("26:00-30:00") }))
                .Build();

            var tue1am = new DateTime(2026, 5, 12, 1, 0, 0);
            var tue2am = new DateTime(2026, 5, 12, 2, 0, 0);
            var tue3am = new DateTime(2026, 5, 12, 3, 0, 0);

            Assert.False(cal.IsWorkingTime(tue1am, OvertimePolicy.Unlimited));
            Assert.True(cal.IsWorkingTime(tue2am, OvertimePolicy.Unlimited));
            Assert.Equal(tue2am, cal.GetNextWorkingTime(tue1am, OvertimePolicy.Unlimited));
            Assert.Equal(tue3am, cal.AddWorkingTime(tue1am, TimeSpan.FromHours(1), OvertimePolicy.Unlimited));
            Assert.Equal(TimeSpan.FromHours(1), cal.GetWorkingTime(tue1am, tue3am, OvertimePolicy.Unlimited));
            Assert.Equal(tue2am, cal.SubtractWorkingTime(tue3am, TimeSpan.FromHours(1), OvertimePolicy.Unlimited));
            Assert.Throws<InvalidOperationException>(() =>
                cal.SubtractWorkingTime(tue3am, TimeSpan.FromHours(2), OvertimePolicy.Unlimited));
        }
    }
}
