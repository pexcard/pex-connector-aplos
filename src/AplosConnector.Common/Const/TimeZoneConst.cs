﻿using System;

namespace AplosConnector.Common.Const
{
    public static class TimeZoneConst
    {
        public static TimeZoneInfo Est
        {
            get {
                TimeZoneInfo easternTime;
                try
                {
                    easternTime = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); //This fails on Ubuntu, so have to look for it by Id.
                }
                catch
                {
                    easternTime = TimeZoneInfo.FindSystemTimeZoneById("US/Eastern");
                }
                return easternTime;
            }
        }
    }
}
