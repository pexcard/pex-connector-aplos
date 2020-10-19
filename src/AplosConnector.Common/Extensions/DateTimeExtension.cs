using System;
using AplosConnector.Common.Const;

namespace AplosConnector.Common.Extensions
{
    public static class DateTimeExtension
    {
        public static DateTime ToTimeZone(this DateTime _this, TimeZoneInfo tz)
        {
            return TimeZoneInfo.ConvertTime(_this, tz);
        }

        public static DateTime ToEst(this DateTime _this)
        {
            return _this.ToTimeZone(TimeZoneConst.Est);
        }
    }
}
