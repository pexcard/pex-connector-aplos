using System.Collections.Generic;
using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models.Aplos;

namespace AplosConnector.Common.Services.Abstractions
{
    public interface IAplosIntegrationMappingService
    {
        PexAplosApiObject Map(AplosApiAccountDetail account);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiAccountDetail> accounts);
        PexAplosApiObject Map(AplosApiContactDetail contact);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiContactDetail> contacts);
        PexAplosApiObject Map(AplosApiFundDetail fund);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiFundDetail> funds);
        PexAplosApiObject Map(AplosApiTagCategoryDetail tagCategory);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTagCategoryDetail> tagCategories);
        PexAplosApiObject Map(AplosApiTagGroupDetail tagGroup);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTagGroupDetail> tagGroup);
        PexAplosApiObject Map(AplosApiTagDetail tag);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTagDetail> tags);
        PexAplosApiObject Map(AplosApiTaxTagDetail taxTag);
        IEnumerable<PexAplosApiObject> Map(IEnumerable<AplosApiTaxTagDetail> taxTags);
    }
}
