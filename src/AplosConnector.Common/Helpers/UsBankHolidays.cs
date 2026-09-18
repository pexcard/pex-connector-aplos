using System;

namespace AplosConnector.Common.Helpers
{
    /// <summary>
    /// Federal Reserve holiday calendar. Fixed-date holidays falling on a Sunday are observed on the
    /// following Monday; those falling on a Saturday are not observed on Friday.
    /// </summary>
    public static class UsBankHolidays
    {
        public static bool IsBankHoliday(DateTime date)
        {
            var day = date.Date;

            return IsFixedDateHoliday(day, 1, 1)      // New Year's Day
                || IsNthWeekday(day, 1, DayOfWeek.Monday, 3)   // Martin Luther King Jr. Day
                || IsNthWeekday(day, 2, DayOfWeek.Monday, 3)   // Presidents' Day
                || IsLastWeekday(day, 5, DayOfWeek.Monday)     // Memorial Day
                || IsFixedDateHoliday(day, 6, 19)     // Juneteenth
                || IsFixedDateHoliday(day, 7, 4)      // Independence Day
                || IsNthWeekday(day, 9, DayOfWeek.Monday, 1)   // Labor Day
                || IsNthWeekday(day, 10, DayOfWeek.Monday, 2)  // Columbus Day
                || IsFixedDateHoliday(day, 11, 11)    // Veterans Day
                || IsNthWeekday(day, 11, DayOfWeek.Thursday, 4) // Thanksgiving
                || IsFixedDateHoliday(day, 12, 25);   // Christmas
        }

        public static bool IsBusinessDay(DateTime date) =>
            date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsBankHoliday(date);

        private static bool IsFixedDateHoliday(DateTime day, int month, int dayOfMonth)
        {
            if (day.Month == month && day.Day == dayOfMonth)
            {
                return day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday;
            }

            if (day.DayOfWeek != DayOfWeek.Monday)
            {
                return false;
            }

            var previousDay = day.AddDays(-1);
            return previousDay.Month == month && previousDay.Day == dayOfMonth;
        }

        private static bool IsNthWeekday(DateTime day, int month, DayOfWeek dayOfWeek, int occurrence) =>
            day.Month == month
            && day.DayOfWeek == dayOfWeek
            && (day.Day - 1) / 7 + 1 == occurrence;

        private static bool IsLastWeekday(DateTime day, int month, DayOfWeek dayOfWeek) =>
            day.Month == month
            && day.DayOfWeek == dayOfWeek
            && day.AddDays(7).Month != month;
    }
}
