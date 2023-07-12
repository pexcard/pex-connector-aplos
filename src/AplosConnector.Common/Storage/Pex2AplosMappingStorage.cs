using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services.Abstractions;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace AplosConnector.Common.Storage
{
    public class Pex2AplosMappingStorage : AzureTableStorageAbstract
    {
        public const string TABLE_NAME = "Pex2AplosMapping";
        public const string PARTITION_KEY = "Pex2Aplos";

        private readonly IStorageMappingService _storageMappingService;
        private readonly ILogger _logger;


        public Pex2AplosMappingStorage(TableClient tableClient, IStorageMappingService storageMappingService, ILogger logger) : base(tableClient)
        {
            _storageMappingService = storageMappingService;
            _logger = logger;
        }

        public async Task CreateAsync(Pex2AplosMappingModel model, CancellationToken cancellationToken)
        {
            var entity = _storageMappingService.Map(model);
            entity.PartitionKey = PARTITION_KEY;
            entity.RowKey = model.PEXBusinessAcctId.ToString();

            try
            {
                await TableClient.AddEntityAsync(entity, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                throw;
            }
        }

        public async Task DeleteAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            var entity = await GetEntityByBusinessAcctId(pexBusinessAcctId, cancellationToken);

            if (entity != null)
            {
                await TableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, default, cancellationToken);
            }
        }

        public async Task UpdateAsync(Pex2AplosMappingModel model, CancellationToken cancellationToken)
        {
            model.IsManualSync = false; // always reset to false when saving

            var entity = _storageMappingService.Map(model);
            entity.PartitionKey = PARTITION_KEY;
            entity.RowKey = model.PEXBusinessAcctId.ToString();
            entity.ETag = new ETag();

            await TableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, cancellationToken);
        }

        public async Task<Pex2AplosMappingModel> GetByBusinessAcctIdAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            var entity = await GetEntityByBusinessAcctId(pexBusinessAcctId, cancellationToken);
            return _storageMappingService.Map(entity);
        }

        private async Task<Pex2AplosMappingEntity> GetEntityByBusinessAcctId(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            var entity = await TableClient
                .GetEntityAsync<Pex2AplosMappingEntity>(PARTITION_KEY, pexBusinessAcctId.ToString(), null, cancellationToken);

            return entity?.Value;
        }

        public async Task<IEnumerable<Pex2AplosMappingModel>> GetAllMappings(CancellationToken cancellationToken)
        {
            var tableEntities = TableClient.QueryAsync<Pex2AplosMappingEntity>((string)null, null, null, cancellationToken);

            var entities = new List<Pex2AplosMappingEntity>();

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
            {
                entities.AddRange(page.Values);
            }

            return _storageMappingService.Map(entities);
        }
    }
}
