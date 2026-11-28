using System;

namespace WorkTimeCalc.Models
{
    /// <summary>
    /// 期間内の実稼働時間を「定時」と「残業」に分解した内訳を表します。
    /// </summary>
    public readonly struct WorkingTimeBreakdown : IEquatable<WorkingTimeBreakdown>
    {
        /// <summary>
        /// 定時・残業ともに 0 の <see cref="WorkingTimeBreakdown"/> を取得します。
        /// </summary>
        public static WorkingTimeBreakdown Zero => default;

        /// <summary>
        /// 定時稼働時間と残業時間を指定して <see cref="WorkingTimeBreakdown"/> を初期化します。
        /// </summary>
        /// <param name="regular">定時内の実稼働時間。</param>
        /// <param name="overtime">残業として算出された実稼働時間。</param>
        public WorkingTimeBreakdown(TimeSpan regular, TimeSpan overtime)
        {
            Regular = regular;
            Overtime = overtime;
        }

        /// <summary>
        /// 定時内の実稼働時間を取得します。
        /// </summary>
        public TimeSpan Regular { get; }

        /// <summary>
        /// 残業として算出された実稼働時間を取得します。
        /// </summary>
        public TimeSpan Overtime { get; }

        /// <summary>
        /// 定時と残業を合算した合計実稼働時間を取得します。
        /// </summary>
        public TimeSpan Total => Regular + Overtime;

        /// <summary>
        /// 定時および残業の符号を反転した <see cref="WorkingTimeBreakdown"/> を返します。
        /// </summary>
        /// <returns>符号反転された <see cref="WorkingTimeBreakdown"/>。</returns>
        public WorkingTimeBreakdown Negate()
        {
            return new WorkingTimeBreakdown(Regular.Negate(), Overtime.Negate());
        }

        /// <inheritdoc />
        public bool Equals(WorkingTimeBreakdown other)
        {
            return Regular.Equals(other.Regular) && Overtime.Equals(other.Overtime);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is WorkingTimeBreakdown other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Regular.GetHashCode() * 397) ^ Overtime.GetHashCode();
            }
        }

        /// <summary>
        /// 2 つの <see cref="WorkingTimeBreakdown"/> が等しいかどうかを判定します。
        /// </summary>
        public static bool operator ==(WorkingTimeBreakdown left, WorkingTimeBreakdown right) => left.Equals(right);

        /// <summary>
        /// 2 つの <see cref="WorkingTimeBreakdown"/> が等しくないかどうかを判定します。
        /// </summary>
        public static bool operator !=(WorkingTimeBreakdown left, WorkingTimeBreakdown right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Regular: {Regular}, Overtime: {Overtime}, Total: {Total}";
        }
    }
}
