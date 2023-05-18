using PexCard.Api.Client.Core.Interfaces;

namespace AplosConnector.Common.Interfaces
{
    public interface IIdNameEntity : IMatchableEntity
    {
        string Id { get; }
        string DisplayName { get; }
    }
}