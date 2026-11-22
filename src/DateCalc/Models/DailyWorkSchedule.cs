using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DateCalc.Internal;

namespace DateCalc.Models
{
    /// <summary>
    /// 1 稼働日あたりの稼働時間帯と休憩時間を表すイミュータブルなスケジュール定義です。
    /// 日跨ぎ稼働（夜勤）を含め、前日・当日の稼働組み合わせに応じた 4 パターンの日内実効稼働スライスを事前計算します。
    /// </summary>
    public sealed class DailyWorkSchedule
    {
        private readonly WorkTimeRange[] _neitherSlices;
        private readonly WorkTimeRange[] _sameDayOnlySlices;
        private readonly WorkTimeRange[] _spilloverOnlySlices;
        private readonly WorkTimeRange[] _bothDaysSlices;

        /// <summary>
        /// 標準的な日勤スケジュール（08:00-17:00、休憩 12:00-13:00、実稼働 8 時間）を取得します。
        /// </summary>
        public static DailyWorkSchedule StandardDayShift { get; } = new DailyWorkSchedule(
            new[] { new WorkTimeRange(TimeSpan.FromHours(8), TimeSpan.FromHours(17)) },
            new[] { new WorkTimeRange(TimeSpan.FromHours(12), TimeSpan.FromHours(13)) });

        /// <summary>
        /// 稼働時間帯と休憩時間帯を指定して <see cref="DailyWorkSchedule"/> を初期化します。
        /// </summary>
        /// <param name="workingPeriods">1 稼働日における稼働時間帯のコレクション（1 つ以上必須）。</param>
        /// <param name="breaks">休憩時間帯のコレクション（省略可）。重複や隣接する休憩、および夜勤シフトにおける深夜帯の休憩（例: 02:00-03:00）は自動的に正規化されます。</param>
        /// <exception cref="ArgumentNullException"><paramref name="workingPeriods"/> が <c>null</c> の場合にスローされます。</exception>
        /// <exception cref="ArgumentException">稼働時間帯が空の場合、または休憩控除後の実効稼働時間が 0 になる場合にスローされます。</exception>
        public DailyWorkSchedule(
            IEnumerable<WorkTimeRange> workingPeriods,
            IEnumerable<WorkTimeRange>? breaks = null)
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

            WorkingPeriods = new ReadOnlyCollection<WorkTimeRange>(inputPeriods.ToArray());
            Breaks = new ReadOnlyCollection<WorkTimeRange>(inputBreaks.ToArray());

            List<RawSpan> mergedWorking = MergeSpans(ToRawSpans(inputPeriods));
            List<RawSpan> normalizedBreaks = NormalizeBreaks(inputBreaks, mergedWorking);
            List<RawSpan> effectiveSpans = SubtractBreaks(mergedWorking, normalizedBreaks);

            if (effectiveSpans.Count == 0)
            {
                throw new ArgumentException("休憩時間により実効稼働時間が 0 になっています。稼働時間帯または休憩時間を見直してください。", nameof(breaks));
            }

            List<WorkTimeRange> effectiveRanges = ToWorkTimeRanges(effectiveSpans);
            EffectiveWorkingRanges = new ReadOnlyCollection<WorkTimeRange>(effectiveRanges.ToArray());

            long totalTicks = 0;
            bool hasSpillover = false;
            foreach (RawSpan span in effectiveSpans)
            {
                totalTicks += (span.End - span.Start).Ticks;
                if (span.End > TimeFormatHelper.OneDay)
                {
                    hasSpillover = true;
                }
            }

            TotalWorkingDuration = new TimeSpan(totalTicks);
            HasOvernightSpillover = hasSpillover;

            _neitherSlices = Array.Empty<WorkTimeRange>();

            List<RawSpan> sameDaySpans = ClipAndShift(effectiveSpans, TimeSpan.Zero, TimeFormatHelper.OneDay, TimeSpan.Zero);
            _sameDayOnlySlices = ToWorkTimeRanges(MergeSpans(sameDaySpans)).ToArray();

            List<RawSpan> spilloverSpans = ClipAndShift(
                effectiveSpans,
                TimeFormatHelper.OneDay,
                TimeFormatHelper.MaxShiftTime,
                -TimeFormatHelper.OneDay);
            _spilloverOnlySlices = ToWorkTimeRanges(MergeSpans(spilloverSpans)).ToArray();

            var combinedSpans = new List<RawSpan>(sameDaySpans.Count + spilloverSpans.Count);
            combinedSpans.AddRange(spilloverSpans);
            combinedSpans.AddRange(sameDaySpans);
            _bothDaysSlices = ToWorkTimeRanges(MergeSpans(combinedSpans)).ToArray();
        }

        /// <summary>
        /// 登録された稼働時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> WorkingPeriods { get; }

        /// <summary>
        /// 登録された休憩時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> Breaks { get; }

        /// <summary>
        /// 休憩時間を控除し、正規化（ソート・重複統合）された実効稼働時間帯の一覧を取得します。
        /// </summary>
        public IReadOnlyList<WorkTimeRange> EffectiveWorkingRanges { get; }

        /// <summary>
        /// 1 稼働日あたりの合計実稼働時間を取得します。
        /// </summary>
        public TimeSpan TotalWorkingDuration { get; }

        /// <summary>
        /// 翌日の 00:00 以降へ跨ぐ実効稼働時間帯が存在するかどうかを取得します。
        /// </summary>
        public bool HasOvernightSpillover { get; }

        /// <summary>
        /// 前日と当日の稼働有無の組み合わせに応じた、当日（[00:00, 24:00)）の正規化済み稼働スライスを取得します。
        /// </summary>
        internal WorkTimeRange[] GetDaySlices(bool isPreviousDayWorking, bool isCurrentDayWorking)
        {
            if (isPreviousDayWorking)
            {
                return isCurrentDayWorking ? _bothDaysSlices : _spilloverOnlySlices;
            }

            return isCurrentDayWorking ? _sameDayOnlySlices : _neitherSlices;
        }

        private readonly struct RawSpan : IComparable<RawSpan>
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

        private static List<RawSpan> ToRawSpans(List<WorkTimeRange> ranges)
        {
            var result = new List<RawSpan>(ranges.Count);
            foreach (WorkTimeRange range in ranges)
            {
                result.Add(new RawSpan(range.Start, range.End));
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

        private static List<RawSpan> NormalizeBreaks(List<WorkTimeRange> inputBreaks, List<RawSpan> mergedWorking)
        {
            if (inputBreaks.Count == 0)
            {
                return new List<RawSpan>();
            }

            var rawBreaks = new List<RawSpan>(inputBreaks.Count);
            foreach (WorkTimeRange brk in inputBreaks)
            {
                var candidate = new RawSpan(brk.Start, brk.End);
                if (!OverlapsAny(candidate, mergedWorking) && brk.End <= TimeFormatHelper.OneDay)
                {
                    var shifted = new RawSpan(
                        brk.Start + TimeFormatHelper.OneDay,
                        brk.End + TimeFormatHelper.OneDay);

                    if (OverlapsAny(shifted, mergedWorking))
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
            if (breakSpans.Count == 0)
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
