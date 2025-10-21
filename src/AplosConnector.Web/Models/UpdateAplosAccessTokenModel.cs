using System;

namespace AplosConnector.Web.Models
{
    public class UpdateAplosAccessTokenModel
    {
        public string AplosAccessToken { get; set; }
        public DateTime? AplosAccessTokenExpiresAt { get; set; }
    }
}
