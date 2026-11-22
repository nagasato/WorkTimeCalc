using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DateCalc.Holidays;
using Xunit;

namespace DateCalc.Tests
{
    public class HolidayProviderCompositionTests
    {
        [Fact]
        public void CabinetOfficeCsvHolidayProvider_ParsesValidCsvWithHeaderAndBothDateFormats()
        {
            const string csv = """
                国民の祝日・休日月日,国民の祝日・休日名称
                2026/1/1,元日
                2026-01-12,成人の日
                2026/5/6,休日
                2026/9/22,"国民の休日"
                """;

            CabinetOfficeCsvHolidayProvider provider = CabinetOfficeCsvHolidayProvider.FromText(csv);

            Assert.Equal(4, provider.Count);
            Assert.Equal(2026, provider.MinYear);
            Assert.Equal(2026, provider.MaxYear);

            Assert.True(provider.TryGetHoliday(new DateTime(2026, 1, 1), out HolidayInfo? newYear));
            Assert.Equal("元日", newYear!.Name);
            Assert.Equal(HolidayKind.National, newYear.Kind);

            Assert.True(provider.TryGetHoliday(new DateTime(2026, 5, 6), out HolidayInfo? sub));
            Assert.Equal("休日", sub!.Name);
            Assert.Equal(HolidayKind.Substitute, sub.Kind);

            Assert.True(provider.TryGetHoliday(new DateTime(2026, 9, 22), out HolidayInfo? citizen));
            Assert.Equal("国民の休日", citizen!.Name);
            Assert.Equal(HolidayKind.CitizensHoliday, citizen.Kind);

            Assert.False(provider.IsHoliday(new DateTime(2027, 1, 1)));
        }

        [Fact]
        public void CabinetOfficeCsvHolidayProvider_ReadsFromTempFileWithBom()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"datecalc_csv_test_{Guid.NewGuid():N}.csv");
            try
            {
                const string csv = "国民の祝日・休日月日,国民の祝日・休日名称\r\n2026/11/23,勤労感謝の日\r\n";
                File.WriteAllText(tempPath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                CabinetOfficeCsvHolidayProvider provider = CabinetOfficeCsvHolidayProvider.FromFile(tempPath);
                Assert.Equal("勤労感謝の日", provider.GetHolidayName(new DateTime(2026, 11, 23)));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Fact]
        public void CabinetOfficeCsvHolidayProvider_ThrowsFormatExceptionOnInvalidDataRow()
        {
            const string badCsv = "国民の祝日・休日月日,国民の祝日・休日名称\nINVALID_DATE,元日\n";
            Assert.Throws<FormatException>(() => CabinetOfficeCsvHolidayProvider.FromText(badCsv));
        }

        [Fact]
        public void CompositeHolidayProvider_WithYearRangeFallback_PrefersCsvInRangeAndFallsBackOutsideRange()
        {
            // 2026年のみ「仮の改正テスト祝日」をCSVに収録したシナリオ
            const string csv = """
                国民の祝日・休日月日,国民の祝日・休日名称
                2026/1/1,元日
                2026/6/15,特別法改正記念日
                """;
            CabinetOfficeCsvHolidayProvider csvProvider = CabinetOfficeCsvHolidayProvider.FromText(csv);
            var composite = CompositeHolidayProvider.WithYearRangeFallback(csvProvider, JapanHolidayProvider.Instance);

            // 収録範囲内 (2026年) は CSV が完全優先される（CSVにない 2026/2/11 は祝日にならない）
            Assert.True(composite.IsHoliday(new DateTime(2026, 6, 15)));
            Assert.Equal("特別法改正記念日", composite.GetHolidayName(new DateTime(2026, 6, 15)));
            Assert.False(composite.IsHoliday(new DateTime(2026, 2, 11)));
            Assert.Equal(2, composite.GetHolidays(2026).Count);

            // 収録範囲外 (2027年) は JapanHolidayProvider にフォールバックする
            Assert.True(composite.IsHoliday(new DateTime(2027, 2, 11)));
            Assert.Equal("建国記念の日", composite.GetHolidayName(new DateTime(2027, 2, 11)));
        }

        [Fact]
        public void InMemoryAndNullHolidayProviders_BehaveAsExpected()
        {
            var mem = new InMemoryHolidayProvider(new Dictionary<DateTime, string>
            {
                [new DateTime(2026, 8, 15)] = "お盆特別祝日"
            });

            Assert.True(mem.TryGetHoliday(new DateTime(2026, 8, 15, 14, 30, 0), out HolidayInfo? info));
            Assert.Equal(HolidayKind.Custom, info!.Kind);
            Assert.Equal("お盆特別祝日", info.Name);
            Assert.Single(mem.GetHolidays(2026));
            Assert.Empty(mem.GetHolidays(2025));

            Assert.False(NullHolidayProvider.Instance.IsHoliday(new DateTime(2026, 1, 1)));
            Assert.Empty(NullHolidayProvider.Instance.GetHolidays(2026));
        }
    }
}
