using System;

namespace AplosConnector.Common.Models
{
    public static class DateTimeExtensions
    {
        public static DateTime AddWeeks(this DateTime date, int weeks)
        {
            return date.AddDays(7 * weeks);
        }

        public static DateTime ToStartOfDay(this DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, 00, 00, 00, 00);
        }

        public static DateTime ToEndOfDay(this DateTime date)
        {
            return new DateTime(date.Year, date.Month, date.Day, 23, 59, 59, 999);
        }
    }
}
