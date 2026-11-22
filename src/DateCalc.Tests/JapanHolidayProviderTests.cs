using System;
using System.Collections.Generic;
using DateCalc.Holidays;
using Xunit;

namespace DateCalc.Tests
{
    public class JapanHolidayProviderTests
    {
        private readonly JapanHolidayProvider _provider = JapanHolidayProvider.Instance;

        [Fact]
        public void GetHolidays_2026_MatchesExpectedAllHolidaysIncludingGwSubstituteAndSilverWeek()
        {
            IReadOnlyList<HolidayInfo> holidays = _provider.GetHolidays(2026);

            Assert.Equal(18, holidays.Count);
            AssertHoliday(new DateTime(2026, 1, 1), "元日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 1, 12), "成人の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 2, 11), "建国記念の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 2, 23), "天皇誕生日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 3, 20), "春分の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 4, 29), "昭和の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 5, 3), "憲法記念日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 5, 4), "みどりの日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 5, 5), "こどもの日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 5, 6), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2026, 7, 20), "海の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 8, 11), "山の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 9, 21), "敬老の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 9, 22), "国民の休日", HolidayKind.CitizensHoliday);
            AssertHoliday(new DateTime(2026, 9, 23), "秋分の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 10, 12), "スポーツの日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 11, 3), "文化の日", HolidayKind.National);
            AssertHoliday(new DateTime(2026, 11, 23), "勤労感謝の日", HolidayKind.National);

            // 平日が誤って祝日になっていないこと
            Assert.False(_provider.IsHoliday(new DateTime(2026, 5, 1)));
            Assert.False(_provider.IsHoliday(new DateTime(2026, 5, 7)));
            Assert.False(_provider.IsHoliday(new DateTime(2026, 9, 24)));
        }

        [Fact]
        public void GetHolidays_2019_HandlesTenDayGoldenWeekAndNoEmperorsBirthday()
        {
            AssertHoliday(new DateTime(2019, 4, 29), "昭和の日", HolidayKind.National);
            AssertHoliday(new DateTime(2019, 4, 30), "国民の休日", HolidayKind.CitizensHoliday);
            AssertHoliday(new DateTime(2019, 5, 1), "天皇の即位の日", HolidayKind.National);
            AssertHoliday(new DateTime(2019, 5, 2), "国民の休日", HolidayKind.CitizensHoliday);
            AssertHoliday(new DateTime(2019, 5, 3), "憲法記念日", HolidayKind.National);
            AssertHoliday(new DateTime(2019, 5, 4), "みどりの日", HolidayKind.National);
            AssertHoliday(new DateTime(2019, 5, 5), "こどもの日", HolidayKind.National);
            AssertHoliday(new DateTime(2019, 5, 6), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2019, 10, 22), "即位礼正殿の儀", HolidayKind.National);

            // 2019年は天皇誕生日なし
            Assert.False(_provider.IsHoliday(new DateTime(2019, 2, 23)));
            Assert.False(_provider.IsHoliday(new DateTime(2019, 12, 23)));
        }

        [Fact]
        public void GetHolidays_2020And2021_HandlesOlympicSpecialShifts()
        {
            // 2020年東京五輪特措法
            AssertHoliday(new DateTime(2020, 7, 23), "海の日", HolidayKind.National);
            AssertHoliday(new DateTime(2020, 7, 24), "スポーツの日", HolidayKind.National);
            AssertHoliday(new DateTime(2020, 8, 10), "山の日", HolidayKind.National);
            Assert.False(_provider.IsHoliday(new DateTime(2020, 7, 20)));
            Assert.False(_provider.IsHoliday(new DateTime(2020, 8, 11)));
            Assert.False(_provider.IsHoliday(new DateTime(2020, 10, 12)));

            // 2021年東京五輪特措法（8/8 山の日が日曜のため 8/9 が振替休日）
            AssertHoliday(new DateTime(2021, 7, 22), "海の日", HolidayKind.National);
            AssertHoliday(new DateTime(2021, 7, 23), "スポーツの日", HolidayKind.National);
            AssertHoliday(new DateTime(2021, 8, 8), "山の日", HolidayKind.National);
            AssertHoliday(new DateTime(2021, 8, 9), "振替休日", HolidayKind.Substitute);
            Assert.False(_provider.IsHoliday(new DateTime(2021, 7, 19)));
            Assert.False(_provider.IsHoliday(new DateTime(2021, 8, 11)));
            Assert.False(_provider.IsHoliday(new DateTime(2021, 10, 11)));
        }

        [Fact]
        public void GetHolidays_2024To2028_KeySubstituteAndEquinoxDates()
        {
            // 2024年振替休日
            AssertHoliday(new DateTime(2024, 2, 12), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2024, 5, 6), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2024, 8, 12), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2024, 9, 23), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2024, 11, 4), "振替休日", HolidayKind.Substitute);

            // 2025年（5/4日曜のみどりの日が 5/6 火曜へ振替）
            AssertHoliday(new DateTime(2025, 2, 24), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2025, 5, 6), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2025, 9, 23), "秋分の日", HolidayKind.National);

            // 2027年・2028年の春分・秋分・振替休日
            AssertHoliday(new DateTime(2027, 3, 21), "春分の日", HolidayKind.National);
            AssertHoliday(new DateTime(2027, 3, 22), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2027, 9, 23), "秋分の日", HolidayKind.National);
            AssertHoliday(new DateTime(2028, 3, 20), "春分の日", HolidayKind.National);
            AssertHoliday(new DateTime(2028, 9, 22), "秋分の日", HolidayKind.National);
        }

        [Fact]
        public void GetHolidays_HistoricalRuleTransitions_MatchStatutoryHistory()
        {
            // 1999年 vs 2000年（成人の日・体育の日のハッピーマンデー移行）
            AssertHoliday(new DateTime(1999, 1, 15), "成人の日", HolidayKind.National);
            AssertHoliday(new DateTime(1999, 10, 10), "体育の日", HolidayKind.National);
            AssertHoliday(new DateTime(1999, 10, 11), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2000, 1, 10), "成人の日", HolidayKind.National);
            AssertHoliday(new DateTime(2000, 10, 9), "体育の日", HolidayKind.National);

            // 2006年 vs 2007年（4/29のみどりの日→昭和の日、5/4がみどりの日、振替休日繰越）
            AssertHoliday(new DateTime(2006, 4, 29), "みどりの日", HolidayKind.National);
            AssertHoliday(new DateTime(2006, 5, 4), "国民の休日", HolidayKind.CitizensHoliday);

            AssertHoliday(new DateTime(2007, 4, 29), "昭和の日", HolidayKind.National);
            AssertHoliday(new DateTime(2007, 4, 30), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(2007, 5, 4), "みどりの日", HolidayKind.National);

            // 1973年（振替休日の初回: 4/29日曜 -> 4/30月曜）および1976年春分の日(3/20)
            AssertHoliday(new DateTime(1973, 4, 30), "振替休日", HolidayKind.Substitute);
            AssertHoliday(new DateTime(1976, 3, 20), "春分の日", HolidayKind.National);

            // DateOnly 拡張メソッドの動作確認
            Assert.True(_provider.IsHoliday(new DateOnly(2026, 11, 23)));
            Assert.Equal("勤労感謝の日", _provider.GetHolidayName(new DateOnly(2026, 11, 23)));
        }

        private void AssertHoliday(DateTime date, string expectedName, HolidayKind expectedKind)
        {
            Assert.True(_provider.TryGetHoliday(date, out HolidayInfo? info), $"{date:yyyy-MM-dd} は祝日 ({expectedName}) のはずです。");
            Assert.NotNull(info);
            Assert.Equal(expectedName, info!.Name);
            Assert.Equal(expectedKind, info.Kind);
        }
    }
}
