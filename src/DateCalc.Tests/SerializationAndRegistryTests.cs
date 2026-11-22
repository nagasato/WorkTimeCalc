using System;
using System.Collections.Generic;
using DateCalc.Holidays;
using DateCalc.Models;
using DateCalc.Serialization;
using Xunit;

namespace DateCalc.Tests
{
    public class SerializationAndRegistryTests
    {
        [Fact]
        public void JsonRoundTrip_SingleCalendar_PreservesScheduleOverridesRecurringAndCustomHolidays()
        {
            const string json = """
                {
                  "id": "hq-standard",
                  "name": "本社標準カレンダー",
                  "workingPeriods": [
                    { "start": "08:30", "end": "17:30" }
                  ],
                  "breaks": [
                    { "start": "12:00", "end": "13:00" },
                    { "start": "15:00", "end": "15:10" }
                  ],
                  "nonWorkingDaysOfWeek": ["Saturday", "Sunday"],
                  "useJapanHolidays": true,
                  "recurringNonWorkingDays": [
                    { "month": 12, "day": 29, "reason": "年末年始" },
                    { "month": 12, "day": 30, "reason": "年末年始" }
                  ],
                  "dateOverrides": [
                    { "date": "2026-05-04", "isWorkingDay": true, "reason": "GW祝日出勤" },
                    { "date": "2026-08-14", "isWorkingDay": false, "reason": "夏季一斉休暇" }
                  ],
                  "customHolidays": [
                    { "date": "2026-10-01", "name": "都民の日特別休日" }
                  ]
                }
                """;

            WorkCalendar calendar = WorkCalendarSerializer.LoadCalendarFromJson(json);

            Assert.Equal("hq-standard", calendar.Id);
            Assert.Equal("本社標準カレンダー", calendar.Name);
            Assert.Equal(new TimeSpan(7, 50, 0), calendar.Schedule.TotalWorkingDuration);
            Assert.True(calendar.IsWorkingDay(new DateTime(2026, 5, 4)));
            Assert.False(calendar.IsWorkingDay(new DateTime(2026, 8, 14)));
            Assert.False(calendar.IsWorkingDay(new DateTime(2026, 12, 29)));
            Assert.False(calendar.IsWorkingDay(new DateTime(2026, 10, 1)));
            Assert.Equal("都民の日特別休日", calendar.GetDayStatus(new DateTime(2026, 10, 1)).Description);

            // シリアライズして再読み込みしても挙動が同一であること
            string reserialized = WorkCalendarSerializer.Serialize(calendar);
            WorkCalendar reloaded = WorkCalendarSerializer.LoadCalendarFromJson(reserialized);

            Assert.Equal(calendar.Schedule.TotalWorkingDuration, reloaded.Schedule.TotalWorkingDuration);
            Assert.Equal(calendar.IsWorkingDay(new DateTime(2026, 5, 4)), reloaded.IsWorkingDay(new DateTime(2026, 5, 4)));
            Assert.Equal(calendar.IsWorkingDay(new DateTime(2026, 10, 1)), reloaded.IsWorkingDay(new DateTime(2026, 10, 1)));
            Assert.Equal(
                calendar.GetWorkingTime(new DateTime(2026, 5, 1, 8, 30, 0), new DateTime(2026, 5, 8, 17, 30, 0)),
                reloaded.GetWorkingTime(new DateTime(2026, 5, 1, 8, 30, 0), new DateTime(2026, 5, 8, 17, 30, 0)));
        }

        [Fact]
        public void FromDefinition_ThrowsOnDuplicateDateOverrides()
        {
            var definition = new WorkCalendarDefinition
            {
                Id = "dup-test",
                DateOverrides = new List<DateOverrideDefinition>
                {
                    new DateOverrideDefinition { Date = "2026-05-04", IsWorkingDay = true, Reason = "休出" },
                    new DateOverrideDefinition { Date = "2026/05/04", IsWorkingDay = false, Reason = "重複" }
                }
            };

            Assert.Throws<ArgumentException>(() => WorkCalendar.FromDefinition(definition));
        }

        [Fact]
        public void WorkCalendarRegistry_LoadsMultipleCalendarsFromJsonAndSupportsCsvOverride()
        {
            const string jsonArray = """
                [
                  {
                    "id": "factory-day",
                    "name": "第1工場 日勤",
                    "workingPeriods": [{ "start": "08:00", "end": "17:00" }],
                    "breaks": [{ "start": "12:00", "end": "13:00" }]
                  },
                  {
                    "id": "factory-night",
                    "name": "第1工場 夜勤",
                    "workingPeriods": [{ "start": "22:00", "end": "06:00" }],
                    "breaks": [{ "start": "02:00", "end": "03:00" }]
                  }
                ]
                """;

            const string customCsv = """
                国民の祝日・休日月日,国民の祝日・休日名称
                2026/1/1,元日
                2026/6/26,工場独自指定祝日
                """;

            CabinetOfficeCsvHolidayProvider csvProvider = CabinetOfficeCsvHolidayProvider.FromText(customCsv);
            var compositeProvider = CompositeHolidayProvider.WithYearRangeFallback(csvProvider, JapanHolidayProvider.Instance);

            IWorkCalendarProvider registry = WorkCalendarSerializer.LoadRegistryFromJson(jsonArray, compositeProvider);

            Assert.Equal(2, registry.CalendarIds.Count);

            // 大文字小文字を区別しない取得
            IWorkCalendar dayCal = registry.GetCalendar("FACTORY-DAY");
            IWorkCalendar nightCal = registry.GetCalendar("factory-night");

            Assert.Equal(TimeSpan.FromHours(8), dayCal.Schedule.TotalWorkingDuration);
            Assert.Equal(TimeSpan.FromHours(7), nightCal.Schedule.TotalWorkingDuration);
            Assert.False(dayCal.IsWorkingDay(new DateTime(2026, 6, 26)));
            Assert.Equal("工場独自指定祝日", dayCal.GetDayStatus(new DateTime(2026, 6, 26)).Description);

            Assert.False(registry.TryGetCalendar("unknown-id", out IWorkCalendar? missing));
            Assert.Null(missing);
            Assert.Throws<KeyNotFoundException>(() => registry.GetCalendar("unknown-id"));
        }

        [Fact]
        public void WorkCalendarRegistry_ThrowsOnMissingOrDuplicateIds()
        {
            WorkCalendar noId = WorkCalendar.CreateBuilder().Build();
            Assert.Throws<ArgumentException>(() => new WorkCalendarRegistry(noId));

            WorkCalendar cal1 = WorkCalendar.CreateBuilder().WithId("line-a").Build();
            WorkCalendar cal2 = WorkCalendar.CreateBuilder().WithId("LINE-A").Build();
            Assert.Throws<ArgumentException>(() => new WorkCalendarRegistry(cal1, cal2));
        }
    }
}
