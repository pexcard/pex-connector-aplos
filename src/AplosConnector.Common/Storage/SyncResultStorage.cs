using Microsoft.WindowsAzure.Storage.Table;
using AplosConnector.Common.Extensions;
using AplosConnector.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AplosConnector.Common.Entities;

namespace AplosConnector.Core.Storages
{
    public class SyncResultStorage : AzureTableStorageAbstract
    {
        public SyncResultStorage(string connectionString)
            : base(connectionString, "SyncResult", "Pex2Aplos") { }

        public async Task CreateAsync(SyncResultModel model)
        {
            await InitTableAsync();
            var entity = new SyncResultEntity(model)
            {
                PartitionKey = PartitionKey, //<----- We should probably use Business Id here....
                RowKey = $"{model.PEXBusinessAcctId}-{model.SyncType}-{model.CreatedUtc.Ticks}"
            };
            var operation = TableOperation.Insert(entity);
            await Table.ExecuteAsync(operation);
        }

        public async Task<List<SyncResultModel>> GetByBusiness(int businessAcctId)
        {
            await InitTableAsync();
            var query = new TableQuery<SyncResultEntity>() {
                FilterString = $"PEXBusinessAcctId eq {businessAcctId}"
            };

            TableContinuationToken token = null;
            var entities = new List<SyncResultEntity>();
            do
            {
                var queryResult = await Table.ExecuteQuerySegmentedAsync(query, token);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

            entities = entities.OrderByDescending(e => e.CreatedUtc).ToList();

            return entities.ConvertAll(entity => entity.ToModel());
        }

        public async Task<List<SyncResultModel>> GetOldResults(DateTime cutoffDate)
        {
            await InitTableAsync();
            var query = new TableQuery<SyncResultEntity>()
            {
                FilterString = $"CreatedUtc lt datetime'{cutoffDate:yyyy-MM-dd}'"
            };

            TableContinuationToken token = null;
            var entities = new List<SyncResultEntity>();
            do
            {
                var queryResult = await Table.ExecuteQuerySegmentedAsync(query, token);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            } while (token != null);

            entities = entities.OrderBy(e => e.CreatedUtc).ToList();
            return entities.ConvertAll(entity => entity.ToModel());
        }

        public async Task DeleteSyncResult(SyncResultModel model)
        {
            var rowKey = $"{model.PEXBusinessAcctId}-{model.SyncType}-{model.CreatedUtc.ToEst().Ticks}";

            await InitTableAsync();

            var operation = TableOperation.Retrieve<SyncResultEntity>(PartitionKey, rowKey);
            var result = await Table.ExecuteAsync(operation);
            var entity = (SyncResultEntity) result?.Result;
            if (entity != null)
            {
                operation = TableOperation.Delete(entity);
                await Table.ExecuteAsync(operation);
            }
        }
    }
}
