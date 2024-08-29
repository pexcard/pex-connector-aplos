using System.Globalization;
using System.Threading;

namespace System
{
    public static class DateTimeExtensions
    {
        public static bool IsWeekend(this DateTime dateTime)
        {
            return dateTime.DayOfWeek == DayOfWeek.Sunday || dateTime.DayOfWeek == DayOfWeek.Saturday;
        }

        public static bool IsWeekday(this DateTime dateTime)
        {
            return !IsWeekend(dateTime);
        }

        public static DateTime AddWeeks(this DateTime dateTime, int weeks)
        {
            return dateTime.AddDays(weeks * 7);
        }

        public static int DaysUntil(this DateTime startDate, DateTime endDate)
        {
            return (endDate - startDate).Days;
        }

        public static double PartialDaysUntil(this DateTime startDate, DateTime endDate)
        {
            return (endDate - startDate).TotalDays;
        }

        public static int MonthsUntil(this DateTime startDate, DateTime endDate)
        {
            return (12 * (endDate.Year - startDate.Year)) + (endDate.Month - startDate.Month);
        }

        public static bool IsLastDayOfMonth(this DateTime dateTime)
        {
            return dateTime.Day.Equals(DateTime.DaysInMonth(dateTime.Year, dateTime.Month));
        }

        public static DateTime LastDayOfMonth(this DateTime dateTime)
        {
            return dateTime.AddDays(DateTime.DaysInMonth(dateTime.Year, dateTime.Month) - dateTime.Day);
        }

        public static bool IsFirstDayOfMonth(this DateTime dateTime)
        {
            return dateTime.Day.Equals(1);
        }

        public static DateTime FirstDayOfMonth(this DateTime dateTime)
        {
            return dateTime.AddDays(-dateTime.Day + 1);
        }

        public static bool IsLastDayOfFebruary(this DateTime dateTime)
        {
            return dateTime.Month.Equals(2) && dateTime.IsLastDayOfMonth();
        }

        public static bool IsLeapDay(this DateTime dateTime)
        {
            return dateTime.Month.Equals(2) && dateTime.Day.Equals(29) && DateTime.IsLeapYear(dateTime.Year);
        }

        public static bool IsLeapYear(this DateTime dateTime)
        {
            return DateTime.IsLeapYear(dateTime.Year);
        }

        public static int CountLeapDays(this DateTime startDate, DateTime endDate)
        {
            var nonLeapDays = 365 * (endDate.Year - startDate.Year);
            return (endDate - startDate).Days - nonLeapDays;
        }

        public static int DaysInMonth(this DateTime dateTime)
        {
            return DateTime.DaysInMonth(dateTime.Year, dateTime.Month);
        }

        public static DateTime ToTimeZone(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            return TimeZoneInfo.ConvertTime(dateTime, timeZone);
        }

        public static DateTime ToStartOfDay(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzStartOfDay = tzDateTime.Date;

            var utcResult = new DateTimeOffset(tzStartOfDay, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime;

            return utcResult;
        }

        public static DateTime ToEndOfDay(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzEndOfDay = new DateTime(tzDateTime.Year, tzDateTime.Month, tzDateTime.Day, 23, 59, 59, 999, tzDateTime.Kind);

            var utcResult = new DateTimeOffset(tzEndOfDay, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime;

            return utcResult;
        }

        public static DateTime ToStartOfWeek(this DateTime dateTime, TimeZoneInfo timeZone) => ToStartOfWeek(dateTime, timeZone, Thread.CurrentThread.CurrentCulture);

        public static DateTime ToStartOfWeek(this DateTime dateTime, TimeZoneInfo timeZone, CultureInfo culture)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzStartOfWeek = tzDateTime.AddDays(-(int)tzDateTime.DayOfWeek + (int)culture.DateTimeFormat.FirstDayOfWeek);

            var utcResult = new DateTimeOffset(tzStartOfWeek, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime.ToStartOfDay(timeZone);

            return utcResult;
        }

        public static DateTime ToEndOfWeek(this DateTime dateTime, TimeZoneInfo timeZone) => ToEndOfWeek(dateTime, timeZone, Thread.CurrentThread.CurrentCulture);

        public static DateTime ToEndOfWeek(this DateTime dateTime, TimeZoneInfo timeZone, CultureInfo culture)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzEndOfWeek = tzDateTime.AddDays(-(int)tzDateTime.DayOfWeek + (int)culture.DateTimeFormat.FirstDayOfWeek).AddDays(6);

            var utcResult = new DateTimeOffset(tzEndOfWeek, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime.ToEndOfDay(timeZone);

            return utcResult;
        }

        public static DateTime ToStartOfMonth(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzStartOfMonth = new DateTime(tzDateTime.Year, tzDateTime.Month, 1, tzDateTime.Hour, tzDateTime.Minute, tzDateTime.Second, tzDateTime.Millisecond, tzDateTime.Kind);

            var utcResult = new DateTimeOffset(tzStartOfMonth, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime.ToStartOfDay(timeZone);

            return utcResult;
        }

        public static DateTime ToEndOfMonth(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzEndOfMonth = new DateTime(tzDateTime.Year, tzDateTime.Month, DateTime.DaysInMonth(tzDateTime.Year, tzDateTime.Month), tzDateTime.Hour, tzDateTime.Minute, tzDateTime.Second, tzDateTime.Millisecond, tzDateTime.Kind);

            var utcResult = new DateTimeOffset(tzEndOfMonth, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime.ToEndOfDay(timeZone);

            return utcResult;
        }

        public static DateTime ToStartOfYear(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzStartOfYear = new DateTime(tzDateTime.Year, 1, 1, tzDateTime.Hour, tzDateTime.Minute, tzDateTime.Second, tzDateTime.Millisecond, tzDateTime.Kind);

            var utcResult = new DateTimeOffset(tzStartOfYear, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime.ToStartOfDay(timeZone);

            return utcResult;
        }

        public static DateTime ToEndOfYear(this DateTime dateTime, TimeZoneInfo timeZone)
        {
            var tzDateTime = dateTime.ToTimeZone(timeZone);
            var tzEndOfYear = new DateTime(tzDateTime.Year, 12, DateTime.DaysInMonth(tzDateTime.Year, 12), tzDateTime.Hour, tzDateTime.Minute, tzDateTime.Second, tzDateTime.Millisecond, tzDateTime.Kind);

            var utcResult = new DateTimeOffset(tzEndOfYear, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime.ToEndOfDay(timeZone);

            return utcResult;
        }
    }
}
