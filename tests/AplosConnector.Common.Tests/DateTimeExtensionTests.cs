using AplosConnector.Common.Extensions;
using System;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class DateTimeExtensionTests
    {
        [Theory]
        [InlineData("2020-01-13T12:30:00Z", "Eastern Standard Time", "US/Eastern", "2020-01-13T07:30:00")]
        [InlineData("2019-08-13T12:30:00Z", "Eastern Standard Time", "US/Eastern", "2019-08-13T08:30:00")]
        public void ToTimeZone_DataDrivenTests(string inputDateS, string inputTimeZoneName, string inputFallbackTimeZoneName, string expectedDateS)
        {
            //Arrange
            DateTime inputDate = DateTime.Parse(inputDateS);
            DateTime expectedDate = DateTime.Parse(expectedDateS);

            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(inputTimeZoneName); //This fails on Ubuntu, so have to look for it by Id.
            }
            catch
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(inputFallbackTimeZoneName);
            }

            //Act
            DateTime actualDate = inputDate.ToTimeZone(timeZone);

            //Assert
            Assert.Equal(expectedDate, actualDate);
        }

        [Theory]
        [InlineData("2020-01-13T12:30:00Z", "2020-01-13T07:30:00")] //No DST
        [InlineData("2019-08-13T12:30:00Z", "2019-08-13T08:30:00")] //DST
        public void ToEst_DataDrivenTests(string inputDateS, string expectedDateS)
        {
            //Arrange
            DateTime inputDate = DateTime.Parse(inputDateS);
            DateTime expectedDate = DateTime.Parse(expectedDateS);

            //Act
            DateTime actualDate = inputDate.ToEst();

            //Assert
            Assert.Equal(expectedDate, actualDate);
        }
    }
}
