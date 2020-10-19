using System;

namespace AplosConnector.Common.Models
{
    public class PexOAuthSessionModel
    {
        public PexOAuthSessionModel()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public Guid SessionGuid { get; set; }
        public string ExternalToken { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? RevokedUtc { get; set; }
        public DateTime? LastRenewedUtc { get; set; }
        public int PEXBusinessAcctId { get; set; }
    }
}
