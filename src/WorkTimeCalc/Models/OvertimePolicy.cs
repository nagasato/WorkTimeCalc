using System;
using WorkTimeCalc.Internal;

namespace WorkTimeCalc.Models
{
    /// <summary>
    /// 稼働時間計算における残業の扱い方を表す種別です。
    /// </summary>
    public enum OvertimePolicyKind
    {
        /// <summary>
        /// 定時のみを対象とし、残業は含めません。
        /// </summary>
        None = 0,

        /// <summary>
        /// カレンダーに事前定義された残業時間帯（<see cref="DailyWorkSchedule.OvertimePeriods"/>）を含めて計算します。
        /// </summary>
        CalendarDefined = 1,

        /// <summary>
        /// 各稼働日の定時終了後に、1 日あたり指定時間まで残業時間帯を動的に延長して計算します。
        /// 上限を超えた分は翌稼働日へ繰り越されます。
        /// </summary>
        MaxPerDay = 2,

        /// <summary>
        /// 1 日の残業上限を設けず、開始側の稼働日から定時終了後へそのまま延長して計算します（翌日への繰り越しなし）。
        /// </summary>
        Unlimited = 3
    }

    /// <summary>
    /// 稼働時間計算時に定時外の残業をどのように扱うかを指定するポリシーです。
    /// </summary>
    public readonly struct OvertimePolicy : IEquatable<OvertimePolicy>
    {
        private OvertimePolicy(OvertimePolicyKind kind, TimeSpan? maxDurationPerDay)
        {
            Kind = kind;
            MaxDurationPerDay = maxDurationPerDay;
        }

        /// <summary>
        /// 定時のみを対象とする既定のポリシーを取得します。
        /// </summary>
        public static OvertimePolicy None => default;

        /// <summary>
        /// カレンダーに事前定義された残業時間帯を含めるポリシーを取得します。
        /// カレンダーに残業時間帯が定義されていない場合は <see cref="None"/> と同じ動作になります。
        /// </summary>
        public static OvertimePolicy CalendarDefined { get; } =
            new OvertimePolicy(OvertimePolicyKind.CalendarDefined, null);

        /// <summary>
        /// 1 日の残業上限を設けず、定時終了後へそのまま延長するポリシーを取得します（カレンダーの休憩時間帯は適用されます）。
        /// </summary>
        public static OvertimePolicy Unlimited { get; } =
            new OvertimePolicy(OvertimePolicyKind.Unlimited, null);

        /// <summary>
        /// ポリシー種別を取得します。
        /// </summary>
        public OvertimePolicyKind Kind { get; }

        /// <summary>
        /// <see cref="OvertimePolicyKind.MaxPerDay"/> 指定時の 1 日あたり最大残業時間を取得します（それ以外の場合は <c>null</c>）。
        /// </summary>
        public TimeSpan? MaxDurationPerDay { get; }

        /// <summary>
        /// 各稼働日の定時終了後に、1 日あたり最大 <paramref name="maxDuration"/> まで動的に残業を許容するポリシーを生成します。
        /// </summary>
        /// <param name="maxDuration">1 稼働日あたりの最大残業時間（00:00 以上、24:00 以下）。0 の場合は <see cref="None"/> を返します。</param>
        /// <returns>生成された <see cref="OvertimePolicy"/>。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDuration"/> が 0 未満または 24 時間を超える場合にスローされます。</exception>
        public static OvertimePolicy MaxPerDay(TimeSpan maxDuration)
        {
            if (maxDuration < TimeSpan.Zero || maxDuration > TimeFormatHelper.OneDay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDuration),
                    maxDuration,
                    "1 日あたりの最大残業時間は 00:00 以上 24:00 以下で指定してください。");
            }

            if (maxDuration == TimeSpan.Zero)
            {
                return None;
            }

            return new OvertimePolicy(OvertimePolicyKind.MaxPerDay, maxDuration);
        }

        /// <inheritdoc />
        public bool Equals(OvertimePolicy other)
        {
            return Kind == other.Kind && MaxDurationPerDay.Equals(other.MaxDurationPerDay);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is OvertimePolicy other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397) ^ MaxDurationPerDay.GetHashCode();
            }
        }

        /// <summary>
        /// 2 つの <see cref="OvertimePolicy"/> が等しいかどうかを判定します。
        /// </summary>
        public static bool operator ==(OvertimePolicy left, OvertimePolicy right) => left.Equals(right);

        /// <summary>
        /// 2 つの <see cref="OvertimePolicy"/> が等しくないかどうかを判定します。
        /// </summary>
        public static bool operator !=(OvertimePolicy left, OvertimePolicy right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString()
        {
            return Kind switch
            {
                OvertimePolicyKind.None => "None",
                OvertimePolicyKind.CalendarDefined => "CalendarDefined",
                OvertimePolicyKind.MaxPerDay => $"MaxPerDay({MaxDurationPerDay})",
                OvertimePolicyKind.Unlimited => "Unlimited",
                _ => Kind.ToString()
            };
        }
    }
}
