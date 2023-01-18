using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.Azure.Cosmos.Table;
using System.Threading;

namespace AplosConnector.Core.Storages
{
    public class Pex2AplosMappingStorage : AzureTableStorageAbstract
    {
        private readonly IStorageMappingService _storageMappingService;
        private readonly ILogger _logger;

        public Pex2AplosMappingStorage(
            string connectionString,
            IStorageMappingService storageMappingService,
            ILogger logger)
            : base(connectionString, "Pex2AplosMapping", "Pex2Aplos")
        {
            _storageMappingService = storageMappingService;
            _logger = logger;
        }

        public async Task CreateAsync(Pex2AplosMappingModel model, CancellationToken cancellationToken)
        {
            var entity = _storageMappingService.Map(model);
            entity.PartitionKey = PartitionKey;
            entity.RowKey = model.PEXBusinessAcctId.ToString();
            var operation = TableOperation.Insert(entity);
            try
            {
                await Table.ExecuteAsync(operation, cancellationToken);
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
            var operation = TableOperation.Delete(entity);
            await Table.ExecuteAsync(operation, cancellationToken);
        }

        public async Task UpdateAsync(Pex2AplosMappingModel model, CancellationToken cancellationToken)
        {
            model.IsManualSync = false; // always reset to false when saving

            var entity = _storageMappingService.Map(model);
            entity.PartitionKey = PartitionKey;
            entity.RowKey = model.PEXBusinessAcctId.ToString();
            entity.ETag = "*";

            var operation = TableOperation.Merge(entity);
            await Table.ExecuteAsync(operation, cancellationToken);
        }

        public async Task<Pex2AplosMappingModel> GetByBusinessAcctIdAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            var entity = await GetEntityByBusinessAcctId(pexBusinessAcctId, cancellationToken);
            return _storageMappingService.Map(entity);
        }

        private async Task<Pex2AplosMappingEntity> GetEntityByBusinessAcctId(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            var operation = TableOperation.Retrieve<Pex2AplosMappingEntity>(PartitionKey, pexBusinessAcctId.ToString());
            var result = await Table.ExecuteAsync(operation, cancellationToken);
            return (Pex2AplosMappingEntity) result?.Result;
        }

        public async Task<IEnumerable<Pex2AplosMappingModel>> GetAllMappings(CancellationToken cancellationToken)
        {
            TableContinuationToken token = null;
            var entities = new List<Pex2AplosMappingEntity>();
            do
            {
                var queryResult = await Table.ExecuteQuerySegmentedAsync(new TableQuery<Pex2AplosMappingEntity>(), token, cancellationToken);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null && !cancellationToken.IsCancellationRequested);

            return _storageMappingService.Map(entities);
        }
    }
}
