using System;
using Microsoft.WindowsAzure.Storage.Table;
using AplosConnector.Common.Models;

namespace AplosConnector.Common.Entities
{
    public class PexOAuthSessionEntity : TableEntity
    {
        public PexOAuthSessionEntity()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public PexOAuthSessionEntity(PexOAuthSessionModel model, string partitionKey)
        {
            PartitionKey = partitionKey;
            RowKey = model.SessionGuid.ToString();
            SessionGuid = model.SessionGuid;
            ExternalToken = model.ExternalToken;
            CreatedUtc = model.CreatedUtc;
            RevokedUtc = model.RevokedUtc;
            LastRenewedUtc = model.LastRenewedUtc;
            PEXBusinessAcctId = model.PEXBusinessAcctId;
        }

        public int PEXBusinessAcctId { get; set; }
        public Guid SessionGuid { get; set; }
        public string ExternalToken { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? RevokedUtc { get; set; }
        public DateTime? LastRenewedUtc { get; set; }

        public PexOAuthSessionModel ToModel()
        {
            return new PexOAuthSessionModel()
            {
                CreatedUtc = CreatedUtc,
                ExternalToken = ExternalToken,
                LastRenewedUtc = LastRenewedUtc,
                PEXBusinessAcctId = PEXBusinessAcctId,
                RevokedUtc = RevokedUtc,
                SessionGuid = SessionGuid
            };
        }
    }
}
