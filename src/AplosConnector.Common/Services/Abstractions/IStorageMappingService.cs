using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using System.Collections.Generic;

namespace AplosConnector.Common.Services.Abstractions
{
    public interface IStorageMappingService
    {
        Pex2AplosMappingModel Map(Pex2AplosMappingEntity model);
        Pex2AplosMappingEntity Map(Pex2AplosMappingModel model);
        IEnumerable<Pex2AplosMappingModel> Map(IEnumerable<Pex2AplosMappingEntity> models);
    }
}