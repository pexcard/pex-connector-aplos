using System;
using System.Collections.Generic;

namespace AplosConnector.Common.Models
{
    public struct TimePeriod
        : IEquatable<TimePeriod>
    {
        public TimePeriod(DateTime start, DateTime end)
        {
            if (start > end)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "The start date cannot be after the end date.");
            }

            Start = start;
            End = end;
        }

        public DateTime Start { get; }

        public DateTime End { get; }

        public static bool operator ==(TimePeriod? first, TimePeriod? second) => AreEqual(first, second);

        public static bool operator !=(TimePeriod? first, TimePeriod? second) => !AreEqual(first, second);

        public static bool AreEqual(TimePeriod? first, TimePeriod? second)
        {
            if (first is null && second is null)
            {
                return true;
            }
            if (first is null || second is null)
            {
                return false;
            }

            return first.Equals(second);
        }

        public bool Equals(TimePeriod other)
        {
            // equality by "value"
            return Start == other.Start && End == other.End;
        }

        public override bool Equals(object? obj) => (obj is TimePeriod other) && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Start, End);

        public override string ToString() => $"{Start:u} - {End:u}";
    }

    public static class TimePeriodExtensions
    {
        public static IEnumerable<TimePeriod> Batch(this TimePeriod timePeriod, TimeSpan batchSizes)
        {
            if (batchSizes < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSizes), "Batch size must be a positive TimeSpan.");
            }

            IEnumerable<TimePeriod> Batch()
            {
                if ((timePeriod.End - timePeriod.Start) <= batchSizes)
                {
                    yield return timePeriod;
                }
                else
                {
                    var start = timePeriod.Start;
                    DateTime? end;

                    do
                    {
                        end = start.Add(batchSizes);

                        if (end > timePeriod.End)
                        {
                            end = timePeriod.End;
                        }

                        yield return new TimePeriod(start, end.Value);

                        start = end.Value.Add(TimeSpan.FromMilliseconds(1));
                    }
                    while (start < timePeriod.End && end < timePeriod.End);
                }
            }

            return Batch();
        }
    }
}
