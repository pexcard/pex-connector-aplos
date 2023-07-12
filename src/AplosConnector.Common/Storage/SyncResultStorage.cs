using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Azure.Data.Tables;

namespace AplosConnector.Common.Storage
{
    public class SyncResultStorage : AzureTableStorageAbstract
    {
        public const string TABLE_NAME = "SyncResult";
        public const string PARTITION_KEY = "Pex2Aplos";

        public SyncResultStorage(TableClient tableClient) : base(tableClient) { }

        public async Task CreateAsync(SyncResultModel model, CancellationToken cancellationToken)
        {
            var entity = new SyncResultEntity(model)
            {
                PartitionKey = PARTITION_KEY, //<----- We should probably use Business Id here....
                RowKey = $"{model.PEXBusinessAcctId}-{model.SyncType}-{model.CreatedUtc.Ticks}"
            };
            await TableClient.AddEntityAsync(entity, cancellationToken);
        }

        public async Task<List<SyncResultModel>> GetByBusiness(int businessAcctId, CancellationToken cancellationToken)
        {
            var tableEntities = TableClient
                .QueryAsync<SyncResultEntity>(f => f.PEXBusinessAcctId == businessAcctId, 500, null, cancellationToken);

            var entities = new List<SyncResultEntity>();

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
            {
                entities.AddRange(page.Values);
            }

            entities = entities.OrderByDescending(e => e.CreatedUtc).ToList();

            return entities.ConvertAll(entity => entity.ToModel());
        }

        public async Task<List<SyncResultModel>> GetOldResults(DateTime cutoffDate, CancellationToken cancellationToken)
        {
            var tableEntities = TableClient
                .QueryAsync<SyncResultEntity>($"CreatedUtc lt datetime'{cutoffDate:yyyy-MM-dd}'", 500, null, cancellationToken);

            var entities = new List<SyncResultEntity>();

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
            {
                entities.AddRange(page.Values);
            }

            entities = entities.OrderByDescending(e => e.CreatedUtc).ToList();

            return entities.ConvertAll(entity => entity.ToModel());
        }

        public async Task DeleteSyncResult(SyncResultModel model, CancellationToken cancellationToken)
        {
            await TableClient.DeleteEntityAsync(PARTITION_KEY, model.Id, default, cancellationToken);
        }
    }
}
