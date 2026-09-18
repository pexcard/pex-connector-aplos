using AplosConnector.Common.Helpers;
using System;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class UsBankHolidaysTests
    {
        [Theory]
        // New Year's Day
        [InlineData("2026-01-01")]
        [InlineData("2027-01-01")]
        // Martin Luther King Jr. Day (3rd Monday in January)
        [InlineData("2026-01-19")]
        [InlineData("2027-01-18")]
        // Presidents' Day (3rd Monday in February)
        [InlineData("2026-02-16")]
        [InlineData("2027-02-15")]
        // Memorial Day (last Monday in May)
        [InlineData("2026-05-25")]
        [InlineData("2027-05-31")]
        // Juneteenth
        [InlineData("2026-06-19")]
        [InlineData("2028-06-19")]
        // Independence Day
        [InlineData("2028-07-04")]
        [InlineData("2029-07-04")]
        // Labor Day (1st Monday in September)
        [InlineData("2026-09-07")]
        [InlineData("2027-09-06")]
        // Columbus Day (2nd Monday in October)
        [InlineData("2026-10-12")]
        [InlineData("2027-10-11")]
        // Veterans Day
        [InlineData("2026-11-11")]
        [InlineData("2027-11-11")]
        // Thanksgiving (4th Thursday in November)
        [InlineData("2026-11-26")]
        [InlineData("2027-11-25")]
        // Christmas
        [InlineData("2026-12-25")]
        [InlineData("2028-12-25")]
        public void IsBankHoliday_ReturnsTrue_OnFederalReserveHolidays(string date)
        {
            Assert.True(UsBankHolidays.IsBankHoliday(DateTime.Parse(date)));
        }

        [Theory]
        [InlineData("2027-07-05")] // Independence Day 2027 falls on Sunday -> observed Monday
        [InlineData("2027-01-01")] // New Year's Day 2027 is a Friday (regular)
        [InlineData("2028-12-25")] // Christmas 2028 is a Monday (regular)
        [InlineData("2033-12-26")] // Christmas 2033 falls on Sunday -> observed Monday
        public void IsBankHoliday_ObservesSundayHolidaysOnTheFollowingMonday(string date)
        {
            Assert.True(UsBankHolidays.IsBankHoliday(DateTime.Parse(date)));
        }

        [Theory]
        [InlineData("2026-07-04")] // Saturday: not observed
        [InlineData("2026-07-03")] // Friday before a Saturday holiday: regular banking day
        [InlineData("2026-07-06")] // Monday after a Saturday holiday: regular banking day
        [InlineData("2027-12-24")] // Friday before Christmas on Saturday
        [InlineData("2026-01-12")] // 2nd Monday in January (not MLK)
        [InlineData("2026-11-19")] // 3rd Thursday in November (not Thanksgiving)
        [InlineData("2026-05-18")] // 2nd-to-last Monday in May (not Memorial Day)
        [InlineData("2026-08-05")] // ordinary Wednesday
        public void IsBankHoliday_ReturnsFalse_OnRegularDaysAndSaturdayHolidays(string date)
        {
            Assert.False(UsBankHolidays.IsBankHoliday(DateTime.Parse(date)));
        }

        [Theory]
        [InlineData("2026-09-04", 3, "2026-09-10")] // Labor Day skipped
        [InlineData("2026-11-25", 3, "2026-12-01")] // Thanksgiving + weekend skipped
        [InlineData("2027-07-02", 1, "2027-07-06")] // Sunday holiday observed Monday
        [InlineData("2026-07-02", 1, "2026-07-03")] // Saturday holiday not observed
        public void AddBusinessDays_SkipsWeekendsAndBankHolidays(string start, int days, string expected)
        {
            var result = Services.AplosIntegrationService.AddBusinessDays(DateTime.Parse(start), days);

            Assert.Equal(DateTime.Parse(expected), result);
        }
    }
}
