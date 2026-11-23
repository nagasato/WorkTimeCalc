using System;
using WorkTimeCalc.Models;
using Xunit;

namespace WorkTimeCalc.Tests
{
    public class DailyWorkScheduleTests
    {
        [Fact]
        public void StandardDayShift_WithMultipleBreaks_ComputesEffectiveRangesAndTotalDuration()
        {
            var schedule = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("08:00-17:00") },
                new[]
                {
                    WorkTimeRange.Parse("12:00-13:00"),
                    WorkTimeRange.Parse("15:00-15:10")
                });

            Assert.Equal(3, schedule.EffectiveWorkingRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("08:00-12:00"), schedule.EffectiveWorkingRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("13:00-15:00"), schedule.EffectiveWorkingRanges[1]);
            Assert.Equal(WorkTimeRange.Parse("15:10-17:00"), schedule.EffectiveWorkingRanges[2]);
            Assert.Equal(new TimeSpan(7, 50, 0), schedule.TotalWorkingDuration);
            Assert.False(schedule.HasOvernightSpillover);
        }

        [Fact]
        public void OverlappingAndOutOfBoundsBreaks_AreNormalizedCorrectly()
        {
            var schedule = new DailyWorkSchedule(
                new[]
                {
                    WorkTimeRange.Parse("09:00-12:00"),
                    WorkTimeRange.Parse("12:00-18:00")
                },
                new[]
                {
                    WorkTimeRange.Parse("08:00-09:30"),
                    WorkTimeRange.Parse("12:00-13:00"),
                    WorkTimeRange.Parse("12:30-13:30"),
                    WorkTimeRange.Parse("17:30-19:00")
                });

            Assert.Equal(2, schedule.EffectiveWorkingRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("09:30-12:00"), schedule.EffectiveWorkingRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("13:30-17:30"), schedule.EffectiveWorkingRanges[1]);
            Assert.Equal(TimeSpan.FromHours(6.5), schedule.TotalWorkingDuration);
        }

        [Fact]
        public void BreaksCoveringEntireWorkingHours_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("08:00-17:00") },
                new[] { WorkTimeRange.Parse("07:00-18:00") }));
        }

        [Fact]
        public void OvernightShift_NormalizesMidnightBreakAndBehavesAcrossAllFourDayStates()
        {
            // 22:00-06:00 の夜勤に対し、02:00-03:00 の休憩を指定（自動的に翌日 26:00-27:00 として解釈）
            var schedule = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("22:00-06:00") },
                new[] { WorkTimeRange.Parse("02:00-03:00") });

            Assert.True(schedule.HasOvernightSpillover);
            Assert.Equal(TimeSpan.FromHours(7), schedule.TotalWorkingDuration);
            Assert.Equal(2, schedule.EffectiveWorkingRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("22:00-26:00"), schedule.EffectiveWorkingRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("27:00-30:00"), schedule.EffectiveWorkingRanges[1]);

            // 土日休みのカレンダーに適用して 4状態 (日=休/休, 月=休/稼, 金=稼/稼, 土=稼/休) の1日分スライスを検証
            WorkCalendar cal = WorkCalendar.CreateBuilder()
                .WithSchedule(schedule)
                .WithoutHolidays()
                .Build();

            // 2026-05-10(日): 前日(土)休・当日(日)休 -> 空
            Assert.Empty(cal.GetWorkingIntervals(new DateTime(2026, 5, 10), new DateTime(2026, 5, 11)));

            // 2026-05-11(月): 前日(日)休・当日(月)稼 -> [22:00-24:00)
            var mon = new DateTime(2026, 5, 11);
            var monIntervals = cal.GetWorkingIntervals(mon, mon.AddDays(1));
            Assert.Single(monIntervals);
            Assert.Equal(new DateTimeInterval(mon.AddHours(22), mon.AddDays(1)), monIntervals[0]);

            // 2026-05-09(土): 前日(金)稼・当日(土)休 -> [00:00-02:00), [03:00-06:00)
            var sat = new DateTime(2026, 5, 9);
            var satIntervals = cal.GetWorkingIntervals(sat, sat.AddDays(1));
            Assert.Equal(2, satIntervals.Count);
            Assert.Equal(new DateTimeInterval(sat, sat.AddHours(2)), satIntervals[0]);
            Assert.Equal(new DateTimeInterval(sat.AddHours(3), sat.AddHours(6)), satIntervals[1]);

            // 2026-05-08(金): 前日(木)稼・当日(金)稼 -> [00:00-02:00), [03:00-06:00), [22:00-24:00)
            var fri = new DateTime(2026, 5, 8);
            var friIntervals = cal.GetWorkingIntervals(fri, fri.AddDays(1));
            Assert.Equal(3, friIntervals.Count);
            Assert.Equal(new DateTimeInterval(fri, fri.AddHours(2)), friIntervals[0]);
            Assert.Equal(new DateTimeInterval(fri.AddHours(3), fri.AddHours(6)), friIntervals[1]);
            Assert.Equal(new DateTimeInterval(fri.AddHours(22), fri.AddDays(1)), friIntervals[2]);
        }

        [Fact]
        public void OvernightShift_WithExplicit26HourAndMidnightCrossingBreak_WorksIdentically()
        {
            var explicitBreakSchedule = new DailyWorkSchedule(
                new[] { WorkTimeRange.Parse("22:00-30:00") },
                new[] { WorkTimeRange.Parse("23:30-00:30") });

            Assert.Equal(TimeSpan.FromHours(7), explicitBreakSchedule.TotalWorkingDuration);
            Assert.Equal(2, explicitBreakSchedule.EffectiveWorkingRanges.Count);
            Assert.Equal(WorkTimeRange.Parse("22:00-23:30"), explicitBreakSchedule.EffectiveWorkingRanges[0]);
            Assert.Equal(WorkTimeRange.Parse("24:30-30:00"), explicitBreakSchedule.EffectiveWorkingRanges[1]);
        }

        [Fact]
        public void Full24HourShift_WithoutBreaks_HasNoOvernightSpillover()
        {
            var fullDay = new DailyWorkSchedule(new[] { WorkTimeRange.Parse("00:00-24:00") });

            Assert.False(fullDay.HasOvernightSpillover);
            Assert.Equal(TimeSpan.FromHours(24), fullDay.TotalWorkingDuration);
            Assert.Single(fullDay.EffectiveWorkingRanges);
            Assert.Equal(WorkTimeRange.Parse("00:00-24:00"), fullDay.EffectiveWorkingRanges[0]);
        }
    }
}
