using AplosConnector.Common.Interfaces;
using PexCard.Api.Client.Core.Interfaces;

namespace AplosConnector.Common.Models
{
    public class AplosVendorExpenseTotalModel : IIdNameEntity
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string Name => DisplayName;

        public decimal Total { get; set; }

        string IMatchableEntity.EntityId => Id;
        string IMatchableEntity.EntityName => DisplayName;
    }
}
