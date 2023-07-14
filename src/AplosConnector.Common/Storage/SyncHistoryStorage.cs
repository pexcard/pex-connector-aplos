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
    public class SyncHistoryStorage : AzureTableStorageAbstract
    {
        public const string TABLE_NAME = "SyncHistory";

        public SyncHistoryStorage(TableClient tableClient) : base(tableClient) { }

        public async Task CreateAsync(SyncResultModel model, CancellationToken cancellationToken)
        {
            var entity = new SyncHistoryEntity(model)
            {
                PartitionKey = model.PEXBusinessAcctId.ToString(),
                RowKey = $"{model.PEXBusinessAcctId}-{model.SyncType}-{model.CreatedUtc.Ticks}"
            };
            await TableClient.AddEntityAsync(entity, cancellationToken);
        }

        public async Task<List<SyncResultModel>> GetByBusiness(int businessAcctId, CancellationToken cancellationToken)
        {
            var tableEntities = TableClient
                .QueryAsync<SyncHistoryEntity>(f => f.PartitionKey == businessAcctId.ToString(), 500, null, cancellationToken);

            var entities = new List<SyncHistoryEntity>();

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
                .QueryAsync<SyncHistoryEntity>($"CreatedUtc lt datetime'{cutoffDate:yyyy-MM-dd}'", 500, null, cancellationToken);

            var entities = new List<SyncHistoryEntity>();

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
            {
                entities.AddRange(page.Values);
            }

            entities = entities.OrderByDescending(e => e.CreatedUtc).ToList();

            return entities.ConvertAll(entity => entity.ToModel());
        }

        public async Task DeleteSyncResult(SyncResultModel model, CancellationToken cancellationToken)
        {
            await TableClient.DeleteEntityAsync(model.PEXBusinessAcctId.ToString(), model.Id, default, cancellationToken);
        }
    }
}
