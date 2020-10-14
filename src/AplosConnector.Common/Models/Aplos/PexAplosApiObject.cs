using PexCard.Api.Client.Core.Interfaces;

namespace AplosConnector.Common.Models.Aplos
{
    public class PexAplosApiObject : IMatchableEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public string EntityId => Id;
        public string EntityName => Name;
    }
}
