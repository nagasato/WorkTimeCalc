using System;
using System.Collections.Generic;
using System.Linq;
using WorkTimeCalc.Models;
using Xunit;

namespace WorkTimeCalc.Tests
{
    public class WorkCalendarDayTests
    {
        [Fact]
        public void PrecedenceOrder_RespectsOverridesOverRecurringHolidaysPublicHolidaysAndWeekends()
        {
            WorkCalendar calendar = WorkCalendar.CreateBuilder()
                .AddRecurringNonWorkingDay(12, 30, "年末休業")
                .AddRecurringNonWorkingDay(5, 8, "創立記念日")
                // 2026-05-03(日曜・憲法記念日) と 2026-05-04(月曜・みどりの日) を休日出勤として稼働日に上書き
                .AddWorkingDayOverride(new DateTime(2026, 5, 3), "GW特別稼働")
                .AddWorkingDayOverride(new DateTime(2026, 5, 4), "GW祝日出勤")
                // 毎年固定休日 (5/8) でも特定年だけ稼働日に再上書き可能
                .AddWorkingDayOverride(new DateTime(2026, 5, 8), "創立記念日振替出社")
                // 通常平日 (2026-05-07) を特別休業日に上書き
                .AddNonWorkingDayOverride(new DateTime(2026, 5, 7), "社内行事休業")
                .Build();

            // 1. 個別日付上書き（稼働）が週末・祝日・毎年固定休日に勝つ
            DayStatus may3 = calendar.GetDayStatus(new DateTime(2026, 5, 3));
            Assert.True(may3.IsWorkingDay);
            Assert.Equal(DayStatusReason.OverrideWorkingDay, may3.Reason);
            Assert.Equal("GW特別稼働", may3.Description);

            DayStatus may4 = calendar.GetDayStatus(new DateTime(2026, 5, 4));
            Assert.True(may4.IsWorkingDay);
            Assert.Equal(DayStatusReason.OverrideWorkingDay, may4.Reason);

            DayStatus may8 = calendar.GetDayStatus(new DateTime(2026, 5, 8));
            Assert.True(may8.IsWorkingDay);
            Assert.Equal(DayStatusReason.OverrideWorkingDay, may8.Reason);

            // 2. 個別日付上書き（非稼働）が平日に勝つ
            DayStatus may7 = calendar.GetDayStatus(new DateTime(2026, 5, 7));
            Assert.False(may7.IsWorkingDay);
            Assert.Equal(DayStatusReason.OverrideNonWorkingDay, may7.Reason);
            Assert.Equal("社内行事休業", may7.Description);

            // 3. 毎年固定の会社休日（上書きなしの年は休み）
            DayStatus dec30 = calendar.GetDayStatus(new DateTime(2026, 12, 30));
            Assert.False(dec30.IsWorkingDay);
            Assert.Equal(DayStatusReason.RecurringHoliday, dec30.Reason);
            Assert.Equal("年末休業", dec30.Description);

            DayStatus may8NextYear = calendar.GetDayStatus(new DateTime(2027, 5, 8));
            Assert.False(may8NextYear.IsWorkingDay);
            Assert.Equal(DayStatusReason.RecurringHoliday, may8NextYear.Reason);

            // 4. 国民の祝日・振替休日
            DayStatus may6 = calendar.GetDayStatus(new DateTime(2026, 5, 6));
            Assert.False(may6.IsWorkingDay);
            Assert.Equal(DayStatusReason.PublicHoliday, may6.Reason);
            Assert.Equal("振替休日", may6.Description);

            // 5. 定休曜日
            DayStatus may2 = calendar.GetDayStatus(new DateTime(2026, 5, 2));
            Assert.False(may2.IsWorkingDay);
            Assert.Equal(DayStatusReason.Weekend, may2.Reason);

            // 6. 通常稼働日
            DayStatus may1 = calendar.GetDayStatus(new DateTime(2026, 5, 1));
            Assert.True(may1.IsWorkingDay);
            Assert.Equal(DayStatusReason.RegularWorkingDay, may1.Reason);
        }

        [Fact]
        public void AddWorkingDays_PositiveNegativeZeroAndNonWorkingStart_Across2026GoldenWeek()
        {
            // 2026/05/01(金)=稼働, 02(土)〜06(水・振替休日)=非稼働, 07(木), 08(金)=稼働
            WorkCalendar calendar = WorkCalendar.CreateDefault();

            // 金曜 + 1稼働日 = 木曜 (5/7)
            Assert.Equal(new DateTime(2026, 5, 7), calendar.AddWorkingDays(new DateTime(2026, 5, 1), 1));
            // 土曜（非稼働） + 1稼働日 = 木曜 (5/7)
            Assert.Equal(new DateTime(2026, 5, 7), calendar.AddWorkingDays(new DateTime(2026, 5, 2), 1));
            // 祝日水曜（非稼働） + 2稼働日 = 金曜 (5/8)
            Assert.Equal(new DateTime(2026, 5, 8), calendar.AddWorkingDays(new DateTime(2026, 5, 6), 2));

            // 逆算: 木曜(5/7) - 1稼働日 = 金曜(5/1)
            Assert.Equal(new DateTime(2026, 5, 1), calendar.AddWorkingDays(new DateTime(2026, 5, 7), -1));
            // 逆算: 日曜(5/3) - 1稼働日 = 金曜(5/1)
            Assert.Equal(new DateTime(2026, 5, 1), calendar.AddWorkingDays(new DateTime(2026, 5, 3), -1));

            // 0稼働日加算時は基準日（時刻切り捨て、Kind保持）
            var utcWithTime = new DateTime(2026, 5, 3, 15, 30, 0, DateTimeKind.Utc);
            DateTime zeroResult = calendar.AddWorkingDays(utcWithTime, 0);
            Assert.Equal(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc), zeroResult);
            Assert.Equal(DateTimeKind.Utc, zeroResult.Kind);
        }

        [Fact]
        public void NextAndPreviousWorkingDay_RespectIncludeSelfFlag()
        {
            WorkCalendar calendar = WorkCalendar.CreateDefault();

            // 5/1(金)は稼働日
            Assert.Equal(new DateTime(2026, 5, 1), calendar.GetNextWorkingDay(new DateTime(2026, 5, 1), includeSelf: true));
            Assert.Equal(new DateTime(2026, 5, 7), calendar.GetNextWorkingDay(new DateTime(2026, 5, 1), includeSelf: false));

            // 5/4(月・祝)は非稼働日
            Assert.Equal(new DateTime(2026, 5, 7), calendar.GetNextWorkingDay(new DateTime(2026, 5, 4), includeSelf: true));
            Assert.Equal(new DateTime(2026, 5, 1), calendar.GetPreviousWorkingDay(new DateTime(2026, 5, 4), includeSelf: true));

            // 5/7(木)から includeSelf: false で前稼働日
            Assert.Equal(new DateTime(2026, 5, 1), calendar.GetPreviousWorkingDay(new DateTime(2026, 5, 7), includeSelf: false));
        }

        [Fact]
        public void CountWorkingDays_And_GetWorkingDays_AreInclusiveAndValidateRange()
        {
            WorkCalendar calendar = WorkCalendar.CreateDefault();

            // 2026/05/01(金) 〜 2026/05/10(日): 稼働日は 5/1, 5/7, 5/8 の 3日間
            var start = new DateTime(2026, 5, 1);
            var end = new DateTime(2026, 5, 10);

            Assert.Equal(3, calendar.CountWorkingDays(start, end));
            List<DateTime> workingDays = calendar.GetWorkingDays(start, end).ToList();
            Assert.Equal(
                new[] { new DateTime(2026, 5, 1), new DateTime(2026, 5, 7), new DateTime(2026, 5, 8) },
                workingDays);

            // 同一日の閉区間
            Assert.Equal(1, calendar.CountWorkingDays(start, start));
            Assert.Equal(0, calendar.CountWorkingDays(new DateTime(2026, 5, 2), new DateTime(2026, 5, 6)));

            // 終了日 < 開始日 は例外
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.CountWorkingDays(end, start));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.GetWorkingDays(end, start));
        }

        [Fact]
        public void DateOnlyOverloads_WorkOnNet8()
        {
            WorkCalendar calendar = WorkCalendar.CreateBuilder()
                .AddWorkingDayOverride(new DateOnly(2026, 5, 4), "祝日特別稼働")
                .Build();

            Assert.True(calendar.IsWorkingDay(new DateOnly(2026, 5, 4)));
            Assert.Equal(DayStatusReason.OverrideWorkingDay, calendar.GetDayStatus(new DateOnly(2026, 5, 4)).Reason);
            Assert.Equal(new DateOnly(2026, 5, 4), calendar.AddWorkingDays(new DateOnly(2026, 5, 1), 1));
            Assert.Equal(new DateOnly(2026, 5, 7), calendar.GetNextWorkingDay(new DateOnly(2026, 5, 5)));
            Assert.Equal(new DateOnly(2026, 5, 4), calendar.GetPreviousWorkingDay(new DateOnly(2026, 5, 6)));
            Assert.Equal(4, calendar.CountWorkingDays(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 10)));
            Assert.Equal(4, calendar.GetWorkingDays(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 10)).Count());
        }

        [Fact]
        public void ZeroWorkingDayCalendar_ThrowsInvalidOperationExceptionWithoutInfiniteLoop()
        {
            WorkCalendar neverWorks = WorkCalendar.CreateBuilder()
                .WithNonWorkingDaysOfWeek(
                    DayOfWeek.Sunday,
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday)
                .Build();

            Assert.Throws<InvalidOperationException>(() => neverWorks.AddWorkingDays(new DateTime(2026, 5, 1), 1));
            Assert.Throws<InvalidOperationException>(() => neverWorks.GetNextWorkingDay(new DateTime(2026, 5, 1)));
            Assert.Throws<InvalidOperationException>(() => neverWorks.GetPreviousWorkingDay(new DateTime(2026, 5, 1)));
            Assert.Throws<InvalidOperationException>(() => neverWorks.AddWorkingTime(new DateTime(2026, 5, 1, 9, 0, 0), TimeSpan.FromHours(1)));
        }
    }
}
