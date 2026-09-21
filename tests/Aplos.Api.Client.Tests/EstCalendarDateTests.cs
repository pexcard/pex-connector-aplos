using System;
using System.Globalization;
using Xunit;

namespace Aplos.Api.Client.Tests
{
    public class EstCalendarDateTests
    {
        [Theory]
        [InlineData("2026-01-16T04:59:59Z", "2026-01-15")] //Standard time, one second before EST midnight
        [InlineData("2026-01-16T05:00:00Z", "2026-01-16")] //Standard time, EST midnight
        [InlineData("2026-07-16T03:59:59Z", "2026-07-15")] //Daylight time, one second before EDT midnight
        [InlineData("2026-07-16T04:00:00Z", "2026-07-16")] //Daylight time, EDT midnight
        [InlineData("2026-03-08T04:59:59Z", "2026-03-07")] //Spring forward day, still EST before midnight
        [InlineData("2026-03-08T05:00:00Z", "2026-03-08")] //Spring forward day, EST midnight
        [InlineData("2026-03-09T03:59:59Z", "2026-03-08")] //Day after spring forward, offset is now -4
        [InlineData("2026-03-09T04:00:00Z", "2026-03-09")] //Day after spring forward, EDT midnight
        [InlineData("2026-11-01T03:59:59Z", "2026-10-31")] //Fall back day, still EDT before midnight
        [InlineData("2026-11-01T04:00:00Z", "2026-11-01")] //Fall back day, EDT midnight
        [InlineData("2026-11-02T04:59:59Z", "2026-11-01")] //Day after fall back, offset is back to -5
        [InlineData("2026-11-02T05:00:00Z", "2026-11-02")] //Day after fall back, EST midnight
        public void ToEstCalendarDate_DataDrivenTests(string inputUtcS, string expectedDateS)
        {
            //Arrange
            var inputUtc = ParseUtc(inputUtcS);
            var expectedDate = DateTime.ParseExact(expectedDateS, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            //Act
            var actualDate = inputUtc.ToEstCalendarDate();

            //Assert
            Assert.Equal(expectedDate, actualDate);
        }

        // Proves the function is not Kind-sensitive. Cannot prove host-independence - on a UTC host it passes
        // either way.
        [Theory]
        [InlineData("2026-01-16T04:59:59")]  //standard time, the boundary case
        [InlineData("2026-07-16T03:59:59")]  //daylight time
        [InlineData("2026-03-08T04:59:59")]  //spring forward
        [InlineData("2026-11-01T03:59:59")]  //fall back
        public void AnUnspecifiedInputIsTreatedTheSameAsAUtcOne(string value)
        {
            var unspecified = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
            Assert.Equal(DateTimeKind.Unspecified, unspecified.Kind);

            var utc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);

            Assert.Equal(utc.ToEstCalendarDate(), unspecified.ToEstCalendarDate());
        }

        internal static DateTime ParseUtc(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
    }
}
