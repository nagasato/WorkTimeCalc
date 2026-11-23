using System;
using System.Collections.Generic;

namespace WorkTimeCalc.Holidays
{
    /// <summary>
    /// 祝日判定および年別祝日一覧を提供するプロバイダのインターフェースです。
    /// </summary>
    public interface IHolidayProvider
    {
        /// <summary>
        /// 指定した日付が祝日である場合にその情報を取得します。
        /// </summary>
        /// <param name="date">判定する日付（時刻成分は無視されます）。</param>
        /// <param name="holiday">祝日が見つかった場合はその情報、見つからない場合は <c>null</c>。</param>
        /// <returns>祝日の場合は <c>true</c>、それ以外は <c>false</c>。</returns>
        bool TryGetHoliday(DateTime date, out HolidayInfo? holiday);

        /// <summary>
        /// 指定した西暦年の祝日一覧を日付昇順で取得します。
        /// </summary>
        /// <param name="year">西暦年（1〜9999）。</param>
        /// <returns>その年の祝日一覧。</returns>
        IReadOnlyList<HolidayInfo> GetHolidays(int year);
    }

    /// <summary>
    /// <see cref="IHolidayProvider"/> の利便性向上のための拡張メソッドを提供します。
    /// </summary>
    public static class HolidayProviderExtensions
    {
        /// <summary>
        /// 指定した日付が祝日かどうかを判定します。
        /// </summary>
        /// <param name="provider">祝日プロバイダ。</param>
        /// <param name="date">判定する日付。</param>
        /// <returns>祝日の場合は <c>true</c>。</returns>
        public static bool IsHoliday(this IHolidayProvider provider, DateTime date)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return provider.TryGetHoliday(date, out _);
        }

        /// <summary>
        /// 指定した日付の祝日名を取得します。祝日でない場合は <c>null</c> を返します。
        /// </summary>
        /// <param name="provider">祝日プロバイダ。</param>
        /// <param name="date">判定する日付。</param>
        /// <returns>祝日名、または <c>null</c>。</returns>
        public static string? GetHolidayName(this IHolidayProvider provider, DateTime date)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return provider.TryGetHoliday(date, out HolidayInfo? holiday) ? holiday?.Name : null;
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// 指定した <see cref="DateOnly"/> が祝日である場合にその情報を取得します。
        /// </summary>
        /// <param name="provider">祝日プロバイダ。</param>
        /// <param name="date">判定する日付。</param>
        /// <param name="holiday">祝日が見つかった場合はその情報、見つからない場合は <c>null</c>。</param>
        /// <returns>祝日の場合は <c>true</c>。</returns>
        public static bool TryGetHoliday(this IHolidayProvider provider, DateOnly date, out HolidayInfo? holiday)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return provider.TryGetHoliday(date.ToDateTime(TimeOnly.MinValue), out holiday);
        }

        /// <summary>
        /// 指定した <see cref="DateOnly"/> が祝日かどうかを判定します。
        /// </summary>
        /// <param name="provider">祝日プロバイダ。</param>
        /// <param name="date">判定する日付。</param>
        /// <returns>祝日の場合は <c>true</c>。</returns>
        public static bool IsHoliday(this IHolidayProvider provider, DateOnly date)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return provider.TryGetHoliday(date.ToDateTime(TimeOnly.MinValue), out _);
        }

        /// <summary>
        /// 指定した <see cref="DateOnly"/> の祝日名を取得します。祝日でない場合は <c>null</c> を返します。
        /// </summary>
        /// <param name="provider">祝日プロバイダ。</param>
        /// <param name="date">判定する日付。</param>
        /// <returns>祝日名、または <c>null</c>。</returns>
        public static string? GetHolidayName(this IHolidayProvider provider, DateOnly date)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return provider.TryGetHoliday(date.ToDateTime(TimeOnly.MinValue), out HolidayInfo? holiday) ? holiday?.Name : null;
        }
#endif
    }
}
