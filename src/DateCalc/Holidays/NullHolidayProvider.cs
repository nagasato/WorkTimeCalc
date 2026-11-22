using System;
using System.Collections.Generic;

namespace DateCalc.Holidays
{
    /// <summary>
    /// 祝日判定を行わない（常に祝日なしを返す）祝日プロバイダです。
    /// </summary>
    public sealed class NullHolidayProvider : IHolidayProvider
    {
        /// <summary>
        /// 共有のシングルトンインスタンスを取得します。
        /// </summary>
        public static NullHolidayProvider Instance { get; } = new NullHolidayProvider();

        private NullHolidayProvider()
        {
        }

        /// <inheritdoc />
        public bool TryGetHoliday(DateTime date, out HolidayInfo? holiday)
        {
            holiday = null;
            return false;
        }

        /// <inheritdoc />
        public IReadOnlyList<HolidayInfo> GetHolidays(int year)
        {
            return Array.Empty<HolidayInfo>();
        }
    }
}
