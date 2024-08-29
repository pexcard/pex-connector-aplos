namespace System
{
    public static class TimeZones
    {
        public static readonly TimeZoneInfo UTC = TimeZoneInfo.Utc;
        public static DateTime ToUTC(this DateTime dateTime) => dateTime.ToTimeZone(UTC);
        public static DateTime IsUTC(this DateTime dateTime) => NewDateTime(UTC, dateTime);

        public static readonly TimeZoneInfo EST = GetTimeZoneCrossPlatform("Eastern Standard Time", "US/Eastern");
        public static DateTime ToEST(this DateTime dateTime) => dateTime.ToTimeZone(EST);
        public static DateTime IsEST(this DateTime dateTime) => NewDateTime(EST, dateTime);
        public static DateTime CoalesceFromEST(this DateTime dateTime) => Coalesce(EST, dateTime);

        public static readonly TimeZoneInfo CST = GetTimeZoneCrossPlatform("Central Standard Time", "US/Indiana-Starke");
        public static DateTime ToCST(this DateTime dateTime) => dateTime.ToTimeZone(CST);
        public static DateTime IsCST(this DateTime dateTime) => NewDateTime(CST, dateTime);
        public static DateTime CoalesceFromCST(this DateTime dateTime) => Coalesce(CST, dateTime);

        public static readonly TimeZoneInfo MDT = GetTimeZoneCrossPlatform("Mountain Standard Time", "US/Mountain");
        public static DateTime ToMDT(this DateTime dateTime) => dateTime.ToTimeZone(MDT);
        public static DateTime IsMDT(this DateTime dateTime) => NewDateTime(MDT, dateTime);
        public static DateTime CoalesceFromMDT(this DateTime dateTime) => Coalesce(MDT, dateTime);

        public static readonly TimeZoneInfo PST = GetTimeZoneCrossPlatform("Pacific Standard Time", "US/Pacific");
        public static DateTime ToPST(this DateTime dateTime) => dateTime.ToTimeZone(PST);
        public static DateTime IsPST(this DateTime dateTime) => NewDateTime(PST, dateTime);
        public static DateTime CoalesceFromPST(this DateTime dateTime) => Coalesce(PST, dateTime);

        public static TimeZoneInfo GetUsaTimeZoneByCode(string threeLetterTimeZoneName)
        {
            switch (threeLetterTimeZoneName?.ToUpper())
            {
                case nameof(UTC):
                    return UTC;
                case nameof(EST):
                    return EST;
                case nameof(CST):
                    return CST;
                case nameof(MDT):
                    return MDT;
                case nameof(PST):
                    return PST;
                default:
                    throw new ArgumentOutOfRangeException(nameof(threeLetterTimeZoneName), $"Unexpected time zone name: {threeLetterTimeZoneName}.");
            }
        }

        public static TimeZoneInfo GetTimeZoneCrossPlatform(string systemTimeZoneName)
        {
            return GetTimeZoneCrossPlatform(systemTimeZoneName, systemTimeZoneName);
        }

        public static TimeZoneInfo GetTimeZoneCrossPlatform(string windowsTimeZoneId, string unixTimeZoneId)
        {
            if (string.IsNullOrEmpty(windowsTimeZoneId))
            {
                throw new ArgumentException($"'{nameof(windowsTimeZoneId)}' cannot be null or empty.", nameof(windowsTimeZoneId));
            }
            if (string.IsNullOrEmpty(unixTimeZoneId))
            {
                throw new ArgumentException($"'{nameof(unixTimeZoneId)}' cannot be null or empty.", nameof(unixTimeZoneId));
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);
            }
            catch (Exception)
            {
                // https://github.com/dotnet/runtime/issues/20523
                return TimeZoneInfo.FindSystemTimeZoneById(unixTimeZoneId);
            }
        }

        public static DateTime Coalesce(this TimeZoneInfo timeZone, DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified)
            {
                return dateTime.ToTimeZone(timeZone).ToUniversalTime();
            }
            else if (dateTime.Kind == DateTimeKind.Local)
            {
                return dateTime.ToUniversalTime();
            }
            else
            {
                return dateTime;
            }
        }

        public static DateTime NewDateTime(this TimeZoneInfo timeZone, DateTime dateTime) => NewDateTime(timeZone, dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond);

        public static DateTime NewDateTime(this TimeZoneInfo timeZone, int year, int month, int day)
        {
            var tzDateTime = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

            var utcResult = new DateTimeOffset(tzDateTime, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime;

            return utcResult;
        }

        public static DateTime NewDateTime(this TimeZoneInfo timeZone, int year, int month, int day, int hour, int minute, int second)
        {
            var tzDateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);

            var utcResult = new DateTimeOffset(tzDateTime, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime;

            return utcResult;
        }

        public static DateTime NewDateTime(this TimeZoneInfo timeZone, int year, int month, int day, int hour, int minute, int second, int millisecond)
        {
            var tzDateTime = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);

            var utcResult = new DateTimeOffset(tzDateTime, timeZone.GetUtcOffset(tzDateTime)).UtcDateTime;

            return utcResult;
        }
    }
}
