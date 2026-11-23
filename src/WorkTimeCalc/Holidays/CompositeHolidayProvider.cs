using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WorkTimeCalc.Holidays
{
    /// <summary>
    /// 複数の <see cref="IHolidayProvider"/> を優先順位付きで合成、または年範囲に応じて切り替えて提供するプロバイダです。
    /// </summary>
    public sealed class CompositeHolidayProvider : IHolidayProvider
    {
        private readonly IHolidayProvider[] _providers;
        private readonly IHolidayProvider? _rangedPrimary;
        private readonly IHolidayProvider? _rangedFallback;
        private readonly int _rangeMinYear;
        private readonly int _rangeMaxYear;

        /// <summary>
        /// 指定したプロバイダ配列を先頭から順に優先して合成する <see cref="CompositeHolidayProvider"/> を初期化します。
        /// </summary>
        /// <param name="providers">優先順位順の祝日プロバイダ一覧。</param>
        public CompositeHolidayProvider(params IHolidayProvider[] providers)
            : this((IEnumerable<IHolidayProvider>)providers)
        {
        }

        /// <summary>
        /// 指定したプロバイダ一覧を先頭から順に優先して合成する <see cref="CompositeHolidayProvider"/> を初期化します。
        /// </summary>
        /// <param name="providers">優先順位順の祝日プロバイダ一覧。</param>
        /// <exception cref="ArgumentNullException"><paramref name="providers"/> が <c>null</c> の場合にスローされます。</exception>
        public CompositeHolidayProvider(IEnumerable<IHolidayProvider> providers)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            var list = new List<IHolidayProvider>();
            foreach (IHolidayProvider provider in providers)
            {
                if (provider == null)
                {
                    throw new ArgumentException("プロバイダ一覧に null を含めることはできません。", nameof(providers));
                }

                list.Add(provider);
            }

            _providers = list.ToArray();
        }

        private CompositeHolidayProvider(
            IHolidayProvider rangedPrimary,
            IHolidayProvider rangedFallback,
            int rangeMinYear,
            int rangeMaxYear)
        {
            _providers = Array.Empty<IHolidayProvider>();
            _rangedPrimary = rangedPrimary;
            _rangedFallback = rangedFallback;
            _rangeMinYear = rangeMinYear;
            _rangeMaxYear = rangeMaxYear;
        }

        /// <summary>
        /// 内閣府 CSV プロバイダの収録年範囲（<see cref="CabinetOfficeCsvHolidayProvider.MinYear"/> 〜
        /// <see cref="CabinetOfficeCsvHolidayProvider.MaxYear"/>）では CSV のデータのみを使用し、
        /// 範囲外の年についてはフォールバック先（例: <see cref="JapanHolidayProvider"/>）へ委譲する合成プロバイダを作成します。
        /// </summary>
        /// <param name="primaryCsvProvider">収録年範囲内で優先使用する内閣府 CSV プロバイダ。</param>
        /// <param name="fallbackProvider">収録年範囲外で使用するフォールバックプロバイダ。</param>
        /// <returns>作成された <see cref="CompositeHolidayProvider"/>。</returns>
        /// <exception cref="ArgumentNullException">引数が <c>null</c> の場合にスローされます。</exception>
        public static CompositeHolidayProvider WithYearRangeFallback(
            CabinetOfficeCsvHolidayProvider primaryCsvProvider,
            IHolidayProvider fallbackProvider)
        {
            if (primaryCsvProvider == null)
            {
                throw new ArgumentNullException(nameof(primaryCsvProvider));
            }

            if (fallbackProvider == null)
            {
                throw new ArgumentNullException(nameof(fallbackProvider));
            }

            if (!primaryCsvProvider.MinYear.HasValue || !primaryCsvProvider.MaxYear.HasValue)
            {
                return new CompositeHolidayProvider(fallbackProvider);
            }

            return new CompositeHolidayProvider(
                primaryCsvProvider,
                fallbackProvider,
                primaryCsvProvider.MinYear.Value,
                primaryCsvProvider.MaxYear.Value);
        }

        /// <inheritdoc />
        public bool TryGetHoliday(DateTime date, out HolidayInfo? holiday)
        {
            if (_rangedPrimary != null && _rangedFallback != null)
            {
                IHolidayProvider target = IsInPrimaryRange(date.Year) ? _rangedPrimary : _rangedFallback;
                return target.TryGetHoliday(date, out holiday);
            }

            foreach (IHolidayProvider provider in _providers)
            {
                if (provider.TryGetHoliday(date, out holiday))
                {
                    return true;
                }
            }

            holiday = null;
            return false;
        }

        /// <inheritdoc />
        public IReadOnlyList<HolidayInfo> GetHolidays(int year)
        {
            if (_rangedPrimary != null && _rangedFallback != null)
            {
                IHolidayProvider target = IsInPrimaryRange(year) ? _rangedPrimary : _rangedFallback;
                return target.GetHolidays(year);
            }

            var merged = new Dictionary<DateTime, HolidayInfo>();
            foreach (IHolidayProvider provider in _providers)
            {
                foreach (HolidayInfo info in provider.GetHolidays(year))
                {
                    if (!merged.ContainsKey(info.Date))
                    {
                        merged.Add(info.Date, info);
                    }
                }
            }

            var ordered = new List<HolidayInfo>(merged.Values);
            ordered.Sort((left, right) => left.Date.CompareTo(right.Date));
            return new ReadOnlyCollection<HolidayInfo>(ordered);
        }

        private bool IsInPrimaryRange(int year)
        {
            return year >= _rangeMinYear && year <= _rangeMaxYear;
        }
    }
}
