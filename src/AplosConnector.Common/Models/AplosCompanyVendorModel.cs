using AplosConnector.Common.Interfaces;

namespace AplosConnector.Common.Models
{
    public class AplosCompanyVendorModel : IIdNameEntity
    {
        public string Id { get; set; }
        public string EntityId => Id;
        public string EntityName => DisplayName;
        public string DisplayName { get; set; }
        public string Name => DisplayName;
        public bool Active { get; set; }
        public string AcctNum { get; set; }
    }
}
