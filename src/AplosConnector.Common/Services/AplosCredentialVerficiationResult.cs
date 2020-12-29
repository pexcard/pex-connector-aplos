using System;

namespace AplosConnector.Common.Services
{
    public class AplosCredentialVerficiationResult
    {
        public bool CanObtainAccessToken { get; set; }
        public bool IsPartnerVerified { get; set; }
        public Uri PartnerVerificationUrl { get; set; }
    }
}
