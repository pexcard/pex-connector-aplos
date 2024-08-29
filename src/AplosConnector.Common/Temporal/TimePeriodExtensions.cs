using System.Collections.Generic;

namespace System
{
    public static class TimePeriodExtensions
    {
        public static IEnumerable<TimePeriod> Batch(this TimePeriod timePeriod, TimeSpan batchSize, TimeSpan? batchStep = default)
        {
            if (batchSize < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be a positive TimeSpan.");
            }
            if (batchStep < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(batchStep), "Batch step must be a positive TimeSpan.");
            }

            IEnumerable<TimePeriod> Batch()
            {
                if ((timePeriod.End - timePeriod.Start) <= batchSize)
                {
                    yield return timePeriod;
                }
                else
                {
                    var start = timePeriod.Start;
                    DateTime? end;

                    do
                    {
                        end = start.Add(batchSize);

                        if (end > timePeriod.End)
                        {
                            end = timePeriod.End;
                        }

                        yield return new TimePeriod(start, end.Value);

                        start = end.Value.Add(batchStep.GetValueOrDefault(TimeSpan.FromMilliseconds(1)));
                    }
                    while (start < timePeriod.End && end < timePeriod.End);
                }
            }

            return Batch();
        }
    }
}
