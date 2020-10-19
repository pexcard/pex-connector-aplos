using System;

namespace AplosConnector.Common.Models
{
    public class AplosAccessTokenModel
    {
        public string AplosAccessToken { get; set; }
        public DateTime? AplosAccessTokenExpiresAt { get; set; }
    }
}
