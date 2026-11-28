using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WorkTimeCalc.Internal;

namespace WorkTimeCalc.Models
{
    /// <summary>
    /// 1 稼働日あたりの稼働時間帯・休憩時間・残業時間帯を表すイミュータブルなスケジュール定義です。
    /// 日跨ぎ稼働（夜勤）を含め、前日・当日の稼働組み合わせに応じた 4 パターンの日内実効稼働スライスを事前計算します。
    /// </summary>
    public sealed class DailyWorkSchedule
    {
        private readonly DaySliceSet _regularSliceSet;
        private readonly DaySliceSet _overtimeOnlySliceSet;
        private readonly DaySliceSet _calendarOvertimeSliceSet;
        private readonly DaySliceSet _unlimitedSliceSet;
        private readonly DaySliceSet _unlimitedFirstDaySliceSet;
        private readonly List<WorkTimeRange> _inputBreaks;
        private readonly List<RawSpan> _effectiveWorkingSpans;
        private readonly TimeSpan _lastRegularEnd;

        /// <summary>
        /// 標準的な日勤スケジュール（08:00-17:00、休憩 12:00-13:00、実稼働 8 時間）を取得します。
        /// </summary>
        public static DailyWorkSchedule StandardDayShift { get; } = new DailyWorkSchedule(
            new[] { new WorkTimeRange(TimeSpan.FromHours(8), TimeSpan.FromHours(17)) },
            new[] { new WorkTimeRange(TimeSpan.FromHours(12), TimeSpan.FromHours(13)) });

        /// <summary>
        /// 定時稼働時間帯と休憩時間帯を指定して <see cref="DailyWorkSchedule"/> を初期化します。
        /// </summary>
        /// <param name="workingPeriods">1 稼働日における定時稼働時間帯のコレクション（1 つ以上必須）。</param>
        /// <param name="breaks">休憩時間帯のコレクション（省略可）。重複や隣接する休憩、および夜勤シフトにおける深夜帯の休憩（例: 02:00-03:00）は自動的に正規化されます。</param>
        /// <exception cref="ArgumentNullException"><paramref name="workingPeriods"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">稼働時間帯が空の場合、または休憩控除後の実効稼働時間が 0 になる場合にスローされます。</exception>
        public DailyWorkSchedule(
            IEnumerable<WorkTimeRange> workingPeriods,
            IEnumerable<WorkTimeRange>? breaks = null)
            : this(workingPeriods, breaks, overtimePeriods: null)
        {
        }

        /// <summary>
        /// 定時稼働時間帯・休憩時間帯・残業時間帯を指定して <see cref="DailyWorkSchedule"/> を初期化します。
        /// </summary>
        /// <param name="workingPeriods">1 稼働日における定時稼働時間帯のコレクション（1 つ以上必須）。</param>
        /// <param name="breaks">休憩時間帯のコレクション（省略可）。定時・残業の両方に適用されます。</param>
        /// <param name="overtimePeriods">事前定義する残業時間帯のコレクション（省略可）。定時と重複する部分は定時が優先されます。</param>
        /// <exception cref="ArgumentNullException"><paramref name="workingPeriods"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">稼働時間帯が空の場合、休憩控除後の実効稼働時間が 0 になる場合、または残業時間帯を指定したにもかかわらず定時・休憩控除後の実効残業時間が 0 になる場合にスローされます。</exception>
        public DailyWorkSchedule(
            IEnumerable<WorkTimeRange> workingPeriods,
            IEnumerable<WorkTimeRange>? breaks,
            IEnumerable<WorkTimeRange>? overtimePeriods)
        {
            if (workingPeriods == null)
            {
                throw new ArgumentNullException(nameof(workingPeriods));
            }

            var inputPeriods = new List<WorkTimeRange>();
            foreach (WorkTimeRange period in workingPeriods)
            {
                inputPeriods.Add(period);
            }

            if (inputPeriods.Count == 0)
            {
                throw new ArgumentException("稼働時間帯は少なくとも 1 つ以上指定する必要があります。", nameof(workingPeriods));
            }

            var inputBreaks = new List<WorkTimeRange>();
            if (breaks != null)
            {
                foreach (WorkTimeRange brk in breaks)
                {
                    inputBreaks.Add(brk);
                }
            }

            var inputOvertime = new List<WorkTimeRange>();
            if (overtimePeriods != null)
            {
                foreach (WorkTimeRange ot in overtimePeriods)
                {
                    inputOvertime.Add(ot);
                }
            }

            WorkingPeriods = new ReadOnlyCollection<WorkTimeRange>(inputPeriods.ToArray());
            Breaks = new ReadOnlyCollection<WorkTimeRange>(inputBreaks.ToArray());
            OvertimePeriods = new ReadOnlyCollection<WorkTimeRange>(inputOvertime.ToArray());
            _inputBreaks = inputBreaks;

            // 1. 定時用の休憩正規化は従来どおり定時時間帯のみを基準に行う（残業枠で定時が変わらないようにする）
            List<RawSpan> mergedWorking = MergeSpans(ToRawSpans(inputPeriods));
            List<RawSpan> regularBreaks = NormalizeBreaks(inputBreaks, mergedWorking);
            List<RawSpan> effectiveWorkingSpans = SubtractBreaks(mergedWorking, regularBreaks);

            if (effectiveWorkingSpans.Count == 0)
            {
                throw new ArgumentException("休憩時間により実効稼働時間が 0 になっています。稼働時間帯または休憩時間を見直してください。", nameof(breaks));
            }

            // 2. 残業時間帯は定時を優先控除し、残業帯を基準に正規化した休憩を控除する
            List<RawSpan> mergedOvertimeRaw = MergeSpans(ToRawSpans(inputOvertime));
            List<RawSpan> effectiveOvertimeSpans;
            if (mergedOvertimeRaw.Count > 0)
            {
                List<RawSpan> overtimeWithoutRegular = SubtractBreaks(mergedOvertimeRaw, mergedWorking);
                List<RawSpan> overtimeBreaks = NormalizeBreaks(inputBreaks, overtimeWithoutRegular);
                effectiveOvertimeSpans = SubtractBreaks(overtimeWithoutRegular, overtimeBreaks);
                if (effectiveOvertimeSpans.Count == 0)
                {
                    throw new ArgumentException(
                        "定時時間帯または休憩時間により実効残業時間が 0 になっています。残業時間帯または休憩時間を見直してください。",
                        nameof(overtimePeriods));
                }
            }
            else
            {
                effectiveOvertimeSpans = new List<RawSpan>();
            }

            _effectiveWorkingSpans = effectiveWorkingSpans;
            FirstRegularStart = effectiveWorkingSpans[0].Start;
            _lastRegularEnd = mergedWorking[mergedWorking.Count - 1].End;

            TotalWorkingDuration = SumDuration(effectiveWorkingSpans);
            TotalOvertimeDuration = SumDuration(effectiveOvertimeSpans);
            TotalWorkingDurationWithOvertime = TotalWorkingDuration + TotalOvertimeDuration;
            HasOvertime = effectiveOvertimeSpans.Count > 0;

            _regularSliceSet = BuildSliceSet(effectiveWorkingSpans);
            EffectiveWorkingRanges = _regularSliceSet.EffectiveRanges;
            HasOvernightSpillover = _regularSliceSet.HasOvernightSpillover;

            if (effectiveOvertimeSpans.Count > 0)
            {
                _overtimeOnlySliceSet = BuildSliceSet(effectiveOvertimeSpans);

                var combinedEffective = new List<RawSpan>(effectiveWorkingSpans.Count + effectiveOvertimeSpans.Count);
                combinedEffective.AddRange(effectiveWorkingSpans);
                combinedEffective.AddRange(effectiveOvertimeSpans);
                List<RawSpan> mergedTotalSpans = MergeSpans(combinedEffective);

                _calendarOvertimeSliceSet = BuildSliceSet(mergedTotalSpans);
                EffectiveOvertimeRanges = _overtimeOnlySliceSet.EffectiveRanges;
                EffectiveTotalWorkingRanges = _calendarOvertimeSliceSet.EffectiveRanges;
                HasOvernightSpilloverWithOvertime = _calendarOvertimeSliceSet.HasOvernightSpillover;
            }
            else
            {
                _overtimeOnlySliceSet = DaySliceSet.Empty;
                _calendarOvertimeSliceSet = _regularSliceSet;
                EffectiveOvertimeRanges = Array.Empty<WorkTimeRange>();
                EffectiveTotalWorkingRanges = EffectiveWorkingRanges;
                HasOvernightSpilloverWithOvertime = HasOvernightSpillover;
            }

            // 3. Unlimited 用スライスセット:
            //    定時エンベロープ内は実効定時区間（分割シフトの隙間・定時休憩）を厳密に保持し、
            //    定時エンベロープ外の延長部分のみ日内投影休憩を控除する。
            //    始業日当日（Day 0）は当日休憩 [00:00, 24:00) のみを適用し、翌日以降の休憩は Day 1 以降で適用する。
            var regularEnvelope = new List<RawSpan>(1)
            {
                new RawSpan(mergedWorking[0].Start, mergedWorking[mergedWorking.Count - 1].End)
            };
            DaySliceSet envelopeSliceSet = BuildSliceSet(regularEnvelope);
            List<RawSpan> sameDayBreaks = BuildSameDayBreakSpans(inputBreaks);
            List<RawSpan> extensionDailyBreaks = BuildDailyBreakSpans(inputBreaks);

            _unlimitedSliceSet = BuildUnlimitedSliceSet(_regularSliceSet, envelopeSliceSet, extensionDailyBreaks);
            if (AreRawSpanListsEqual(sameDayBreaks, extensionDailyBreaks))
            {
                _unlimitedFirstDaySliceSet = _unlimitedSliceSet;
                HasDistinctFirstDayUnlimitedSlices = false;
            }
            else
            {
                _unlimitedFirstDaySliceSet = BuildUnlimitedSliceSet(_regularSliceSet, envelopeSliceSet, sameDayBreaks);
                HasDistinctFirstDayUnlimitedSlices = true;
            }
        }

        /// <summary>
        /// 登録された定時稼働時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> WorkingPeriods { get; }

        /// <summary>
        /// 登録された休憩時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> Breaks { get; }

        /// <summary>
        /// 登録された残業時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> OvertimePeriods { get; }

        /// <summary>
        /// 休憩時間を控除し、正規化（ソート・重複統合）された実効定時稼働時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> EffectiveWorkingRanges { get; }

        /// <summary>
        /// 定時重複および休憩時間を控除し、正規化された実効残業時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> EffectiveOvertimeRanges { get; }

        /// <summary>
        /// 定時と残業を結合・正規化した実効稼働時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> EffectiveTotalWorkingRanges { get; }

        /// <summary>
        /// 1 稼働日あたりの合計実定時稼働時間を取得します。
        /// </summary>
        public TimeSpan TotalWorkingDuration { get; }

        /// <summary>
        /// 1 稼働日あたりの合計実残業時間を取得します（事前定義がない場合は <see cref="TimeSpan.Zero"/>）。
        /// </summary>
        public TimeSpan TotalOvertimeDuration { get; }

        /// <summary>
        /// 定時と事前定義残業を合算した 1 稼働日あたりの最大合計実稼働時間を取得します。
        /// </summary>
        public TimeSpan TotalWorkingDurationWithOvertime { get; }

        /// <summary>
        /// 事前定義された残業時間帯が存在するかどうかを取得します。
        /// </summary>
        public bool HasOvertime { get; }

        /// <summary>
        /// 定時稼働時間帯において翌日の 00:00 以降へ跨ぐ実効稼働時間帯が存在するかどうかを取得します。
        /// </summary>
        public bool HasOvernightSpillover { get; }

        /// <summary>
        /// 事前定義残業を含めた場合に翌日の 00:00 以降へ跨ぐ実効稼働時間帯が存在するかどうかを取得します。
        /// </summary>
        public bool HasOvernightSpilloverWithOvertime { get; }

        internal DaySliceSet RegularSliceSet => _regularSliceSet;

        internal DaySliceSet OvertimeOnlySliceSet => _overtimeOnlySliceSet;

        internal DaySliceSet CalendarOvertimeSliceSet => _calendarOvertimeSliceSet;

        internal DaySliceSet UnlimitedSliceSet => _unlimitedSliceSet;

        internal DaySliceSet UnlimitedFirstDaySliceSet => _unlimitedFirstDaySliceSet;

        internal bool HasDistinctFirstDayUnlimitedSlices { get; }

        internal bool HasUnlimitedNonWorkingDaySlices => _unlimitedSliceSet.NeitherSlices.Length > 0;

        internal TimeSpan FirstRegularStart { get; }

        /// <summary>
        /// 前日と当日の稼働有無の組み合わせに応じた、当日（[00:00, 24:00)）の正規化済み定時稼働スライスを取得します。
        /// </summary>
        internal WorkTimeRange[] GetDaySlices(bool isPreviousDayWorking, bool isCurrentDayWorking)
        {
            return _regularSliceSet.GetDaySlices(isPreviousDayWorking, isCurrentDayWorking);
        }

        /// <summary>
        /// 定時終了後に 1 日あたり最大 <paramref name="maxOvertime"/> までの動的残業を延長したスライスセットを構築します。
        /// 48:00 を超える複数日跨ぎの残業延長にも対応します。
        /// </summary>
        internal DaySliceSet BuildDynamicOvertimeSliceSet(TimeSpan maxOvertime)
        {
            if (maxOvertime <= TimeSpan.Zero)
            {
                return _regularSliceSet;
            }

            var dynamicOvertimeSpans = new List<RawSpan>();
            TimeSpan cursor = _lastRegularEnd;
            long remainingTicks = maxOvertime.Ticks;
            TimeSpan windowLength = TimeFormatHelper.MaxShiftTime; // 48時間単位で休憩を展開

            while (remainingTicks > 0)
            {
                long ticksBeforeWindow = remainingTicks;
                TimeSpan windowEnd = TimeSpan.MaxValue - cursor > windowLength
                    ? cursor + windowLength
                    : TimeSpan.MaxValue;
                List<RawSpan> windowBreaks = BuildBreakSpansInWindow(_inputBreaks, cursor, windowEnd);
                int breakIndex = 0;

                while (remainingTicks > 0 && cursor < windowEnd)
                {
                    while (breakIndex < windowBreaks.Count && windowBreaks[breakIndex].End <= cursor)
                    {
                        breakIndex++;
                    }

                    if (breakIndex < windowBreaks.Count && windowBreaks[breakIndex].Start <= cursor)
                    {
                        cursor = windowBreaks[breakIndex].End;
                        continue;
                    }

                    TimeSpan segmentEnd = (breakIndex < windowBreaks.Count && windowBreaks[breakIndex].Start < windowEnd)
                        ? windowBreaks[breakIndex].Start
                        : windowEnd;

                    long availableTicks = (segmentEnd - cursor).Ticks;
                    if (availableTicks <= 0)
                    {
                        break;
                    }

                    long takeTicks = remainingTicks < availableTicks ? remainingTicks : availableTicks;
                    TimeSpan nextCursor = cursor + TimeSpan.FromTicks(takeTicks);
                    dynamicOvertimeSpans.Add(new RawSpan(cursor, nextCursor));
                    cursor = nextCursor;
                    remainingTicks -= takeTicks;
                }

                if (remainingTicks > 0 &&
                    ((remainingTicks == ticksBeforeWindow && cursor >= TimeFormatHelper.OneDay) ||
                     windowEnd == TimeSpan.MaxValue))
                {
                    throw new InvalidOperationException(
                        "休憩時間により終日が覆われているため、指定された残業上限分の稼働時間を確保できません。");
                }
            }

            if (dynamicOvertimeSpans.Count == 0)
            {
                return _regularSliceSet;
            }

            var combined = new List<RawSpan>(_effectiveWorkingSpans.Count + dynamicOvertimeSpans.Count);
            combined.AddRange(_effectiveWorkingSpans);
            combined.AddRange(dynamicOvertimeSpans);
            return BuildSliceSet(MergeSpans(combined));
        }

        internal readonly struct RawSpan : IComparable<RawSpan>
        {
            public RawSpan(TimeSpan start, TimeSpan end)
            {
                Start = start;
                End = end;
            }

            public TimeSpan Start { get; }

            public TimeSpan End { get; }

            public int CompareTo(RawSpan other)
            {
                int cmp = Start.CompareTo(other.Start);
                return cmp != 0 ? cmp : End.CompareTo(other.End);
            }
        }

        internal sealed class DaySliceSet
        {
            private readonly List<RawSpan>[]? _slicesByDayOffset;

            public static DaySliceSet Empty { get; } = new DaySliceSet(
                Array.Empty<RawSpan>(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                maxSpilloverDays: 0,
                slicesByDayOffset: null);

            public DaySliceSet(
                RawSpan[] effectiveSpans,
                WorkTimeRange[] neitherSlices,
                WorkTimeRange[] sameDayOnlySlices,
                WorkTimeRange[] spilloverOnlySlices,
                WorkTimeRange[] bothDaysSlices,
                IReadOnlyList<WorkTimeRange> effectiveRanges,
                int maxSpilloverDays,
                List<RawSpan>[]? slicesByDayOffset)
            {
                EffectiveSpans = effectiveSpans;
                NeitherSlices = neitherSlices;
                SameDayOnlySlices = sameDayOnlySlices;
                SpilloverOnlySlices = spilloverOnlySlices;
                BothDaysSlices = bothDaysSlices;
                EffectiveRanges = effectiveRanges;
                MaxSpilloverDays = maxSpilloverDays;
                _slicesByDayOffset = slicesByDayOffset;
            }

            public RawSpan[] EffectiveSpans { get; }

            public WorkTimeRange[] NeitherSlices { get; }

            public WorkTimeRange[] SameDayOnlySlices { get; }

            public WorkTimeRange[] SpilloverOnlySlices { get; }

            public WorkTimeRange[] BothDaysSlices { get; }

            public IReadOnlyList<WorkTimeRange> EffectiveRanges { get; }

            public int MaxSpilloverDays { get; }

            public bool HasOvernightSpillover => MaxSpilloverDays > 0;

            public WorkTimeRange[] GetDaySlices(bool isPreviousDayWorking, bool isCurrentDayWorking)
            {
                if (isPreviousDayWorking)
                {
                    return isCurrentDayWorking ? BothDaysSlices : SpilloverOnlySlices;
                }

                return isCurrentDayWorking ? SameDayOnlySlices : NeitherSlices;
            }

            public WorkTimeRange[] GetDaySlices(DateTime date, DateTime minSearchDate, Func<DateTime, bool> isWorkingDay)
            {
                if (_slicesByDayOffset == null)
                {
                    bool isCurrentWorking = isWorkingDay(date);
                    bool isPreviousWorking = date > minSearchDate && isWorkingDay(date.AddDays(-1));
                    return GetDaySlices(isPreviousWorking, isCurrentWorking);
                }

                var collected = new List<RawSpan>();
                int availableBackDays = (date - minSearchDate).Days;
                int limit = MaxSpilloverDays < availableBackDays ? MaxSpilloverDays : availableBackDays;

                for (int offset = 0; offset <= limit; offset++)
                {
                    List<RawSpan> daySlices = _slicesByDayOffset[offset];
                    if (daySlices.Count == 0)
                    {
                        continue;
                    }

                    DateTime origin = offset == 0 ? date : date.AddDays(-offset);
                    if (isWorkingDay(origin))
                    {
                        collected.AddRange(daySlices);
                    }
                }

                return collected.Count == 0
                    ? Array.Empty<WorkTimeRange>()
                    : ToWorkTimeRanges(MergeSpans(collected)).ToArray();
            }
        }

        private static DaySliceSet BuildSliceSet(List<RawSpan> effectiveSpans)
        {
            if (effectiveSpans.Count == 0)
            {
                return DaySliceSet.Empty;
            }

            TimeSpan maxEnd = TimeSpan.Zero;
            foreach (RawSpan span in effectiveSpans)
            {
                if (span.End > maxEnd)
                {
                    maxEnd = span.End;
                }
            }

            int maxSpilloverDays = maxEnd <= TimeFormatHelper.OneDay
                ? 0
                : (int)((maxEnd.Ticks - 1) / TimeFormatHelper.OneDay.Ticks);

            IReadOnlyList<WorkTimeRange> effectiveRanges = maxEnd <= TimeFormatHelper.MaxShiftTime
                ? new ReadOnlyCollection<WorkTimeRange>(ToWorkTimeRanges(effectiveSpans).ToArray())
                : Array.Empty<WorkTimeRange>();

            if (maxSpilloverDays <= 1)
            {
                List<RawSpan> sameDaySpans = ClipAndShift(effectiveSpans, TimeSpan.Zero, TimeFormatHelper.OneDay, TimeSpan.Zero);
                WorkTimeRange[] sameDayOnlySlices = ToWorkTimeRanges(MergeSpans(sameDaySpans)).ToArray();

                List<RawSpan> spilloverSpans = ClipAndShift(
                    effectiveSpans,
                    TimeFormatHelper.OneDay,
                    TimeFormatHelper.MaxShiftTime,
                    -TimeFormatHelper.OneDay);
                WorkTimeRange[] spilloverOnlySlices = ToWorkTimeRanges(MergeSpans(spilloverSpans)).ToArray();

                var combinedSpans = new List<RawSpan>(sameDaySpans.Count + spilloverSpans.Count);
                combinedSpans.AddRange(spilloverSpans);
                combinedSpans.AddRange(sameDaySpans);
                WorkTimeRange[] bothDaysSlices = ToWorkTimeRanges(MergeSpans(combinedSpans)).ToArray();

                return new DaySliceSet(
                    effectiveSpans.ToArray(),
                    Array.Empty<WorkTimeRange>(),
                    sameDayOnlySlices,
                    spilloverOnlySlices,
                    bothDaysSlices,
                    effectiveRanges,
                    maxSpilloverDays,
                    slicesByDayOffset: null);
            }

            var slicesByDayOffset = new List<RawSpan>[maxSpilloverDays + 1];
            for (int d = 0; d <= maxSpilloverDays; d++)
            {
                TimeSpan windowStart = TimeSpan.FromTicks(d * TimeFormatHelper.OneDay.Ticks);
                TimeSpan windowEnd = windowStart + TimeFormatHelper.OneDay;
                slicesByDayOffset[d] = MergeSpans(ClipAndShift(effectiveSpans, windowStart, windowEnd, -windowStart));
            }

            return new DaySliceSet(
                effectiveSpans.ToArray(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                Array.Empty<WorkTimeRange>(),
                effectiveRanges,
                maxSpilloverDays,
                slicesByDayOffset);
        }

        private static DaySliceSet BuildUnlimitedSliceSet(
            DaySliceSet regularSliceSet,
            DaySliceSet envelopeSliceSet,
            List<RawSpan> extensionDailyBreaks)
        {
            WorkTimeRange[] neither = BuildUnlimitedStateSlices(
                regularSliceSet.NeitherSlices,
                envelopeSliceSet.NeitherSlices,
                extensionDailyBreaks);
            WorkTimeRange[] sameDay = BuildUnlimitedStateSlices(
                regularSliceSet.SameDayOnlySlices,
                envelopeSliceSet.SameDayOnlySlices,
                extensionDailyBreaks);
            WorkTimeRange[] spillover = BuildUnlimitedStateSlices(
                regularSliceSet.SpilloverOnlySlices,
                envelopeSliceSet.SpilloverOnlySlices,
                extensionDailyBreaks);
            WorkTimeRange[] bothDays = BuildUnlimitedStateSlices(
                regularSliceSet.BothDaysSlices,
                envelopeSliceSet.BothDaysSlices,
                extensionDailyBreaks);

            return new DaySliceSet(
                Array.Empty<RawSpan>(),
                neither,
                sameDay,
                spillover,
                bothDays,
                Array.Empty<WorkTimeRange>(),
                maxSpilloverDays: regularSliceSet.MaxSpilloverDays,
                slicesByDayOffset: null);
        }

        private static WorkTimeRange[] BuildUnlimitedStateSlices(
            WorkTimeRange[] regularStateSlices,
            WorkTimeRange[] envelopeStateSlices,
            List<RawSpan> extensionDailyBreaks)
        {
            var fullDay = new List<RawSpan>(1) { new RawSpan(TimeSpan.Zero, TimeFormatHelper.OneDay) };
            List<RawSpan> envSpans = ToRawSpans(envelopeStateSlices);
            List<RawSpan> outsideEnv = SubtractBreaks(fullDay, envSpans);
            List<RawSpan> extensionSpans = SubtractBreaks(outsideEnv, extensionDailyBreaks);

            List<RawSpan> regSpans = ToRawSpans(regularStateSlices);
            var combined = new List<RawSpan>(regSpans.Count + extensionSpans.Count);
            combined.AddRange(regSpans);
            combined.AddRange(extensionSpans);

            return ToWorkTimeRanges(MergeSpans(combined)).ToArray();
        }

        private static List<RawSpan> BuildSameDayBreakSpans(List<WorkTimeRange> inputBreaks)
        {
            if (inputBreaks.Count == 0)
            {
                return new List<RawSpan>();
            }

            var rawBreaks = ToRawSpans(inputBreaks);
            List<RawSpan> sameDayBreaks = ClipAndShift(rawBreaks, TimeSpan.Zero, TimeFormatHelper.OneDay, TimeSpan.Zero);
            return MergeSpans(sameDayBreaks);
        }

        private static bool AreRawSpanListsEqual(List<RawSpan> left, List<RawSpan> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Start != right[i].Start || left[i].End != right[i].End)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<RawSpan> BuildDailyBreakSpans(List<WorkTimeRange> inputBreaks)
        {
            if (inputBreaks.Count == 0)
            {
                return new List<RawSpan>();
            }

            var rawBreaks = ToRawSpans(inputBreaks);
            List<RawSpan> sameDayBreaks = ClipAndShift(rawBreaks, TimeSpan.Zero, TimeFormatHelper.OneDay, TimeSpan.Zero);
            List<RawSpan> spilloverBreaks = ClipAndShift(
                rawBreaks,
                TimeFormatHelper.OneDay,
                TimeFormatHelper.MaxShiftTime,
                -TimeFormatHelper.OneDay);

            var projected = new List<RawSpan>(sameDayBreaks.Count + spilloverBreaks.Count);
            projected.AddRange(sameDayBreaks);
            projected.AddRange(spilloverBreaks);
            return MergeSpans(projected);
        }

        private static List<RawSpan> BuildBreakSpansInWindow(
            List<WorkTimeRange> inputBreaks,
            TimeSpan windowStart,
            TimeSpan windowEnd)
        {
            if (inputBreaks.Count == 0)
            {
                return new List<RawSpan>();
            }

            long oneDayTicks = TimeFormatHelper.OneDay.Ticks;
            var result = new List<RawSpan>();

            foreach (WorkTimeRange brk in inputBreaks)
            {
                long firstDay = brk.End <= windowStart
                    ? ((windowStart - brk.End).Ticks / oneDayTicks) + 1
                    : 0;

                for (long dayOffset = firstDay; ; dayOffset++)
                {
                    TimeSpan shift = TimeSpan.FromTicks(dayOffset * oneDayTicks);
                    TimeSpan shiftedStart = brk.Start + shift;
                    if (shiftedStart >= windowEnd)
                    {
                        break;
                    }

                    TimeSpan shiftedEnd = brk.End + shift;
                    if (shiftedEnd > windowStart)
                    {
                        result.Add(new RawSpan(shiftedStart, shiftedEnd));
                    }
                }
            }

            return MergeSpans(result);
        }

        private static TimeSpan SumDuration(List<RawSpan> spans)
        {
            long totalTicks = 0;
            foreach (RawSpan span in spans)
            {
                totalTicks += (span.End - span.Start).Ticks;
            }

            return new TimeSpan(totalTicks);
        }

        private static List<RawSpan> ToRawSpans(IReadOnlyList<WorkTimeRange> ranges)
        {
            var result = new List<RawSpan>(ranges.Count);
            for (int i = 0; i < ranges.Count; i++)
            {
                result.Add(new RawSpan(ranges[i].Start, ranges[i].End));
            }

            return result;
        }

        private static List<RawSpan> MergeSpans(List<RawSpan> spans)
        {
            if (spans.Count == 0)
            {
                return new List<RawSpan>();
            }

            spans.Sort();
            var merged = new List<RawSpan>(spans.Count);
            RawSpan current = spans[0];

            for (int i = 1; i < spans.Count; i++)
            {
                RawSpan next = spans[i];
                if (next.Start <= current.End)
                {
                    if (next.End > current.End)
                    {
                        current = new RawSpan(current.Start, next.End);
                    }
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }

        private static List<RawSpan> NormalizeBreaks(List<WorkTimeRange> inputBreaks, List<RawSpan> mergedTargets)
        {
            if (inputBreaks.Count == 0 || mergedTargets.Count == 0)
            {
                return new List<RawSpan>();
            }

            var rawBreaks = new List<RawSpan>(inputBreaks.Count);
            foreach (WorkTimeRange brk in inputBreaks)
            {
                var candidate = new RawSpan(brk.Start, brk.End);
                if (!OverlapsAny(candidate, mergedTargets) && brk.End <= TimeFormatHelper.OneDay)
                {
                    var shifted = new RawSpan(
                        brk.Start + TimeFormatHelper.OneDay,
                        brk.End + TimeFormatHelper.OneDay);

                    if (OverlapsAny(shifted, mergedTargets))
                    {
                        candidate = shifted;
                    }
                }

                rawBreaks.Add(candidate);
            }

            return MergeSpans(rawBreaks);
        }

        private static bool OverlapsAny(RawSpan candidate, List<RawSpan> targets)
        {
            foreach (RawSpan target in targets)
            {
                if (candidate.Start < target.End && candidate.End > target.Start)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<RawSpan> SubtractBreaks(List<RawSpan> workingSpans, List<RawSpan> breakSpans)
        {
            if (breakSpans.Count == 0 || workingSpans.Count == 0)
            {
                return new List<RawSpan>(workingSpans);
            }

            var result = new List<RawSpan>(workingSpans.Count + breakSpans.Count);
            foreach (RawSpan work in workingSpans)
            {
                TimeSpan cursor = work.Start;
                foreach (RawSpan brk in breakSpans)
                {
                    if (brk.End <= cursor)
                    {
                        continue;
                    }

                    if (brk.Start >= work.End)
                    {
                        break;
                    }

                    if (brk.Start > cursor)
                    {
                        result.Add(new RawSpan(cursor, brk.Start));
                    }

                    if (brk.End > cursor)
                    {
                        cursor = brk.End;
                    }

                    if (cursor >= work.End)
                    {
                        break;
                    }
                }

                if (cursor < work.End)
                {
                    result.Add(new RawSpan(cursor, work.End));
                }
            }

            return result;
        }

        private static List<RawSpan> ClipAndShift(
            List<RawSpan> spans,
            TimeSpan windowStart,
            TimeSpan windowEnd,
            TimeSpan shift)
        {
            var result = new List<RawSpan>();
            foreach (RawSpan span in spans)
            {
                TimeSpan start = span.Start > windowStart ? span.Start : windowStart;
                TimeSpan end = span.End < windowEnd ? span.End : windowEnd;
                if (end > start)
                {
                    result.Add(new RawSpan(start + shift, end + shift));
                }
            }

            return result;
        }

        private static List<WorkTimeRange> ToWorkTimeRanges(List<RawSpan> spans)
        {
            var result = new List<WorkTimeRange>(spans.Count);
            foreach (RawSpan span in spans)
            {
                TimeSpan start = span.Start;
                TimeSpan end = span.End;
                if (end - start <= TimeFormatHelper.OneDay)
                {
                    result.Add(new WorkTimeRange(start, end));
                }
                else
                {
                    // 複数時間帯が連結して 24 時間を超えた場合は 24:00 境界で分割する
                    result.Add(new WorkTimeRange(start, TimeFormatHelper.OneDay));
                    result.Add(new WorkTimeRange(TimeFormatHelper.OneDay, end));
                }
            }

            return result;
        }
    }
}
