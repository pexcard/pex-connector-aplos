using System;

namespace Aplos.Api.Client.Models
{
    public class AplosAuthModel
    {
        public string AplosAccessToken { get; set; }
        public DateTime AplosAccessTokenExpiresAt { get; set; }
    }
}
