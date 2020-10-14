using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using AplosConnector.Common.Models;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Services.Abstractions;

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

        public async Task CreateAsync(Pex2AplosMappingModel model)
        {
            await InitTableAsync();
            var entity = _storageMappingService.Map(model);
            entity.PartitionKey = PartitionKey;
            entity.RowKey = model.PEXBusinessAcctId.ToString();
            var operation = TableOperation.Insert(entity);
            try
            {
                await Table.ExecuteAsync(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                throw;
            }

        }

        public async Task DeleteAsync(int pexBusinessAcctId)
        {
            await InitTableAsync();
            var entity = await GetEntityByBusinessAcctId(pexBusinessAcctId);
            var operation = TableOperation.Delete(entity);
            await Table.ExecuteAsync(operation);
        }

        public async Task UpdateAsync(Pex2AplosMappingModel model)
        {
            await InitTableAsync();
            var entity = _storageMappingService.Map(model);
            entity.PartitionKey = PartitionKey;
            entity.RowKey = model.PEXBusinessAcctId.ToString();
            entity.ETag = "*"; //TODO: Fix this later, it prevents concurrency.

            var operation = TableOperation.Merge(entity);
            await Table.ExecuteAsync(operation);
        }

        public async Task<Pex2AplosMappingModel> GetByBusinessAcctIdAsync(int pexBusinessAcctId)
        {
            await InitTableAsync();
            var entity = await GetEntityByBusinessAcctId(pexBusinessAcctId);
            return _storageMappingService.Map(entity);
        }

        private async Task<Pex2AplosMappingEntity> GetEntityByBusinessAcctId(int pexBusinessAcctId)
        {
            var operation = TableOperation.Retrieve<Pex2AplosMappingEntity>(PartitionKey, pexBusinessAcctId.ToString());
            var result = await Table.ExecuteAsync(operation);
            return (Pex2AplosMappingEntity) result?.Result;
        }

        public async Task<IEnumerable<Pex2AplosMappingModel>> GetAllMappings()
        {
            await InitTableAsync();

            TableContinuationToken token = null;
            var entities = new List<Pex2AplosMappingEntity>();
            do
            {
                var queryResult = await Table.ExecuteQuerySegmentedAsync(new TableQuery<Pex2AplosMappingEntity>(), token);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

            return _storageMappingService.Map(entities);
        }
    }
}
