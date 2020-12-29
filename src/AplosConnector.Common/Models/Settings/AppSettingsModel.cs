using System;

namespace AplosConnector.Common.Models.Settings
{
    public class AppSettingsModel
    {
        public Uri PexConnectorBaseURL { get; set; }
        public string PexApiClientId { get; set; }
        public string PexApiClientSecret { get; set; }
        public string AplosConnectorBaseURL { get; set; }
        public Uri AplosApiBaseURL { get; set; }
        public Uri PEXAPIBaseURL { get; set; }
        public string CorsAllowedOrigins { get; set; }

        public string DataProtectionApplicationName { get; set; }
        public string DataProtectionBlobContainer { get; set; }
        public string DataProtectionBlobName { get; set; }
        public string DataProtectionKeyIdentifier { get; set; }

        public bool EnforceAplosPartnerVerification { get; set; }
        public Uri AplosPartnerVerificationUrl { get; set; }
    }
}
