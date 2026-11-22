using System;
using System.Collections.Generic;
using DateCalc.Models;
using Xunit;

namespace DateCalc.Tests
{
    public class WorkCalendarTimeTests
    {
        private static WorkCalendar CreateFactoryDayCalendar()
        {
            // 08:00-17:00, 休憩 12:00-13:00 および 15:00-15:10（実稼働 7時間50分/日）
            return WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeOnly.Parse("08:00"), TimeOnly.Parse("17:00"))
                .AddBreak(TimeOnly.Parse("12:00"), TimeOnly.Parse("13:00"))
                .AddBreak(TimeOnly.Parse("15:00"), TimeOnly.Parse("15:10"))
                .Build();
        }

        private static WorkCalendar CreateNightShiftCalendar()
        {
            // 夜勤 22:00-06:00, 深夜休憩 02:00-03:00（実稼働 7時間/シフト）
            return WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(22), TimeSpan.FromHours(6))
                .AddBreak(TimeSpan.FromHours(2), TimeSpan.FromHours(3))
                .Build();
        }

        [Fact]
        public void DayShift_IsWorkingTime_FollowsHalfOpenIntervalBoundaries()
        {
            WorkCalendar cal = CreateFactoryDayCalendar();

            Assert.False(cal.IsWorkingTime(new DateTime(2026, 5, 1, 7, 59, 59)));
            Assert.True(cal.IsWorkingTime(new DateTime(2026, 5, 1, 8, 0, 0)));
            Assert.True(cal.IsWorkingTime(new DateTime(2026, 5, 1, 11, 59, 59)));
            Assert.False(cal.IsWorkingTime(new DateTime(2026, 5, 1, 12, 0, 0)));
            Assert.True(cal.IsWorkingTime(new DateTime(2026, 5, 1, 13, 0, 0)));
            Assert.False(cal.IsWorkingTime(new DateTime(2026, 5, 1, 15, 0, 0)));
            Assert.True(cal.IsWorkingTime(new DateTime(2026, 5, 1, 15, 10, 0)));
            Assert.False(cal.IsWorkingTime(new DateTime(2026, 5, 1, 17, 0, 0)));

            // 祝日は終日非稼働
            Assert.False(cal.IsWorkingTime(new DateTime(2026, 5, 4, 10, 0, 0)));
        }

        [Fact]
        public void DayShift_AddAndSubtractWorkingTime_AreExactInversesAcrossBreaksAndHolidays()
        {
            WorkCalendar cal = CreateFactoryDayCalendar();
            var start = new DateTime(2026, 5, 1, 8, 0, 0);

            // ちょうど休憩直前で完了する場合は次の休憩を跨がない
            Assert.Equal(new DateTime(2026, 5, 1, 12, 0, 0), cal.AddWorkingTime(start, TimeSpan.FromHours(4)));
            // 1分超えると 13:01
            Assert.Equal(new DateTime(2026, 5, 1, 13, 1, 0), cal.AddWorkingTime(start, new TimeSpan(4, 1, 0)));
            // 7時間50分でちょうど 17:00
            Assert.Equal(new DateTime(2026, 5, 1, 17, 0, 0), cal.AddWorkingTime(start, new TimeSpan(7, 50, 0)));

            // 8時間加算 -> 金曜5/1で 7h50m 消化、GW連休（5/2〜5/6）を跨いで 5/7 08:10 に完了
            DateTime eightHoursLater = cal.AddWorkingTime(start, TimeSpan.FromHours(8));
            Assert.Equal(new DateTime(2026, 5, 7, 8, 10, 0), eightHoursLater);

            // 逆算の対称性確認
            Assert.Equal(start, cal.SubtractWorkingTime(eightHoursLater, TimeSpan.FromHours(8)));
            Assert.Equal(new DateTime(2026, 5, 1, 13, 0, 0), cal.AddWorkingTime(new DateTime(2026, 5, 1, 17, 0, 0), TimeSpan.FromMinutes(-230)));
            Assert.Equal(new DateTime(2026, 5, 1, 15, 10, 0), cal.SubtractWorkingTime(new DateTime(2026, 5, 1, 17, 0, 0), new TimeSpan(1, 50, 0)));

            // TimeSpan.Zero は日時を変えない
            var nonWorkingMoment = new DateTime(2026, 5, 2, 10, 0, 0);
            Assert.Equal(nonWorkingMoment, cal.AddWorkingTime(nonWorkingMoment, TimeSpan.Zero));
            Assert.Equal(nonWorkingMoment, cal.SubtractWorkingTime(nonWorkingMoment, TimeSpan.Zero));
        }

        [Fact]
        public void DayShift_GetWorkingTime_And_NextPreviousWorkingTime_WorkAsExpected()
        {
            WorkCalendar cal = CreateFactoryDayCalendar();

            // 11:30 〜 15:30: [11:30-12:00]=30m + [13:00-15:00]=120m + [15:10-15:30]=20m => 170m (2h50m)
            var t1 = new DateTime(2026, 5, 1, 11, 30, 0);
            var t2 = new DateTime(2026, 5, 1, 15, 30, 0);
            Assert.Equal(new TimeSpan(2, 50, 0), cal.GetWorkingTime(t1, t2));
            // 逆順は負の値
            Assert.Equal(new TimeSpan(-2, -50, 0), cal.GetWorkingTime(t2, t1));
            Assert.Equal(TimeSpan.Zero, cal.GetWorkingTime(t1, t1));

            // 5/1 08:00 〜 5/8 17:00 -> 5/1, 5/7, 5/8 の 3稼働日分 = 23時間30分
            Assert.Equal(
                new TimeSpan(23, 30, 0),
                cal.GetWorkingTime(new DateTime(2026, 5, 1, 8, 0, 0), new DateTime(2026, 5, 8, 17, 0, 0)));

            // GetNextWorkingTime / GetPreviousWorkingTime
            Assert.Equal(new DateTime(2026, 5, 1, 13, 0, 0), cal.GetNextWorkingTime(new DateTime(2026, 5, 1, 12, 0, 0)));
            Assert.Equal(new DateTime(2026, 5, 1, 12, 0, 0), cal.GetPreviousWorkingTime(new DateTime(2026, 5, 1, 12, 30, 0)));
            Assert.Equal(new DateTime(2026, 5, 1, 17, 0, 0), cal.GetPreviousWorkingTime(new DateTime(2026, 5, 3, 10, 0, 0)));
            Assert.Equal(new DateTime(2026, 5, 7, 8, 0, 0), cal.GetNextWorkingTime(new DateTime(2026, 5, 3, 10, 0, 0)));
        }

        [Fact]
        public void OvernightShift_SpilloverAndWeekendBoundaries_BehaveAccurately()
        {
            WorkCalendar night = CreateNightShiftCalendar();

            // 2026-05-08(金)は稼働日、05-09(土)・05-10(日)は非稼働日、05-11(月)は稼働日
            Assert.True(night.IsWorkingDay(new DateTime(2026, 5, 8)));
            Assert.False(night.IsWorkingDay(new DateTime(2026, 5, 9)));
            Assert.False(night.IsWorkingDay(new DateTime(2026, 5, 10)));
            Assert.True(night.IsWorkingDay(new DateTime(2026, 5, 11)));

            // 金曜夜勤の繰り越しで土曜早朝は稼働中（02:00-03:00の深夜休憩を除く）
            Assert.True(night.IsWorkingTime(new DateTime(2026, 5, 9, 1, 0, 0)));
            Assert.False(night.IsWorkingTime(new DateTime(2026, 5, 9, 2, 30, 0)));
            Assert.True(night.IsWorkingTime(new DateTime(2026, 5, 9, 3, 30, 0)));
            Assert.False(night.IsWorkingTime(new DateTime(2026, 5, 9, 6, 0, 0)));

            // 土曜夜と日曜夜には始業がないため、土曜 23:00 も月曜 01:00 も非稼働
            Assert.False(night.IsWorkingTime(new DateTime(2026, 5, 9, 23, 0, 0)));
            Assert.False(night.IsWorkingTime(new DateTime(2026, 5, 11, 1, 0, 0)));
            Assert.True(night.IsWorkingTime(new DateTime(2026, 5, 11, 22, 0, 0)));

            // 金曜 23:00 + 7時間: 5/8[23-24](1h) + 5/9[00-02](2h) + 5/9[03-06](3h) + 5/11[22-23](1h) => 5/11 23:00
            var friNight = new DateTime(2026, 5, 8, 23, 0, 0);
            DateTime monNight = night.AddWorkingTime(friNight, TimeSpan.FromHours(7));
            Assert.Equal(new DateTime(2026, 5, 11, 23, 0, 0), monNight);
            Assert.Equal(friNight, night.SubtractWorkingTime(monNight, TimeSpan.FromHours(7)));

            // GetWorkingTime と GetWorkingIntervals（日跨ぎ境界 [22:00-24:00) + [00:00-02:00) の連続結合）
            IReadOnlyList<DateTimeInterval> intervals = night.GetWorkingIntervals(
                new DateTime(2026, 5, 8, 21, 0, 0),
                new DateTime(2026, 5, 9, 7, 0, 0));

            Assert.Equal(2, intervals.Count);
            Assert.Equal(
                new DateTimeInterval(new DateTime(2026, 5, 8, 22, 0, 0), new DateTime(2026, 5, 9, 2, 0, 0)),
                intervals[0]);
            Assert.Equal(
                new DateTimeInterval(new DateTime(2026, 5, 9, 3, 0, 0), new DateTime(2026, 5, 9, 6, 0, 0)),
                intervals[1]);
            Assert.Equal(
                TimeSpan.FromHours(7),
                night.GetWorkingTime(new DateTime(2026, 5, 8, 21, 0, 0), new DateTime(2026, 5, 9, 7, 0, 0)));

            // GetShiftIntervals（金曜始業分 vs 土曜非稼働日）
            IReadOnlyList<DateTimeInterval> fridayShift = night.GetShiftIntervals(new DateOnly(2026, 5, 8));
            Assert.Equal(2, fridayShift.Count);
            Assert.Empty(night.GetShiftIntervals(new DateOnly(2026, 5, 9)));
        }

        [Fact]
        public void OvernightShift_WithSaturdayWorkingOverride_OperatesContinuouslyAcrossWeekend()
        {
            WorkCalendar nightWithSatWork = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.FromHours(22), TimeSpan.FromHours(6))
                .AddBreak(TimeSpan.FromHours(2), TimeSpan.FromHours(3))
                .AddWorkingDayOverride(new DateTime(2026, 5, 9), "土曜夜勤休出")
                .Build();

            // 土曜夜勤があるため日曜早朝 01:00 は稼働中、日曜夜はないため月曜早朝 01:00 は非稼働
            Assert.True(nightWithSatWork.IsWorkingTime(new DateTime(2026, 5, 9, 23, 0, 0)));
            Assert.True(nightWithSatWork.IsWorkingTime(new DateTime(2026, 5, 10, 1, 0, 0)));
            Assert.False(nightWithSatWork.IsWorkingTime(new DateTime(2026, 5, 11, 1, 0, 0)));

            // 金曜 22:00 〜 日曜 06:00 は金曜夜勤(7h) + 土曜夜勤(7h) = 14時間
            Assert.Equal(
                TimeSpan.FromHours(14),
                nightWithSatWork.GetWorkingTime(new DateTime(2026, 5, 8, 22, 0, 0), new DateTime(2026, 5, 10, 6, 0, 0)));
        }

        [Fact]
        public void Continuous24x7Calendar_PreservesKindAndHandlesMinValueBoundary()
        {
            WorkCalendar allDayEveryDay = WorkCalendar.CreateBuilder()
                .WithWorkingHours(TimeSpan.Zero, TimeSpan.FromHours(24))
                .WithNonWorkingDaysOfWeek()
                .WithoutHolidays()
                .Build();

            var utcStart = new DateTime(2026, 5, 1, 22, 0, 0, DateTimeKind.Utc);
            DateTime utcEnd = allDayEveryDay.AddWorkingTime(utcStart, TimeSpan.FromHours(5));
            Assert.Equal(new DateTime(2026, 5, 2, 3, 0, 0, DateTimeKind.Utc), utcEnd);
            Assert.Equal(DateTimeKind.Utc, utcEnd.Kind);

            // 連続する日跨ぎ区間が 1 つに結合される
            IReadOnlyList<DateTimeInterval> merged = allDayEveryDay.GetWorkingIntervals(utcStart, utcEnd);
            Assert.Single(merged);
            Assert.Equal(new DateTimeInterval(utcStart, utcEnd), merged[0]);

            // DateTime.MinValue 付近でも前日参照で例外が出ないこと
            Assert.True(allDayEveryDay.IsWorkingTime(DateTime.MinValue.AddHours(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                allDayEveryDay.SubtractWorkingTime(DateTime.MinValue.AddMinutes(10), TimeSpan.FromMinutes(20)));
        }
    }
}
