using System;

namespace AplosConnector.Common.Models
{
    public struct DateRange : IEquatable<DateRange>
    {
        public DateRange(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }

        public DateTime Start { get; }

        public DateTime End { get; }

        public bool Equals(DateRange other)
        {
            // equalty by value
            return Start == other.Start && End == other.End;
        }

        public override bool Equals(object obj)
        {
            if (obj is DateRange other)
            {
                return Equals(other);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Start.GetHashCode() + End.GetHashCode();
        }

        public override string ToString()
        {
            return $"{Start:u} - {End:u}";
        }
    }
}
