﻿using System;

namespace AplosConnector.Common.Models.Settings
{
    public class AppSettingsModel
    {
        public Uri PexConnectorBaseURL { get; set; }
        public string PexApiClientId { get; set; }
        public string PexApiClientSecret { get; set; }
        public string AplosConnectorBaseURL { get; set; }
        public Uri AplosApiBaseURL { get; set; }
        public string AplosApiClientId { get; set; }
        public string AplosApiClientSecret { get; set; }
        public Uri PEXAPIBaseURL { get; set; }
        public int PEXAPITimeout { get; set; } = 100;
        public string CorsAllowedOrigins { get; set; }

        public string DataProtectionApplicationName { get; set; }
        public string DataProtectionBlobContainer { get; set; }
        public string DataProtectionBlobName { get; set; }
        public string DataProtectionKeyIdentifier { get; set; }

        public bool EnforceAplosPartnerVerification { get; set; }
        public Uri AplosPartnerVerificationUrl { get; set; }

        public string AzureServiceBusUrl { get; set; }
        public string AzureServiceBusTopicName { get; set; }
        public int EmailMaxCount { get; set; }
        public int EmailPeriodicityDays { get; set; }

        public int SyncTransactionsIntervalDays { get; set; }
    }
}
