using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Azure.Data.Tables;
using Murmur;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Storage
{
    public class SyncHistoryStorage : AzureTableStorageAbstract
    {
        private static readonly HashAlgorithm _hashAlgo = MurmurHash.Create32();

        public const string TABLE_NAME = "SyncHistory";

        public SyncHistoryStorage(TableClient tableClient) : base(tableClient) { }

        public async Task CreateAsync(SyncResultModel model, CancellationToken cancellationToken)
        {
            var entity = new SyncHistoryEntity(model)
            {
                PartitionKey = model.PEXBusinessAcctId.ToString(),
                RowKey = GenerateRowKey(model),
            };
            await TableClient.AddEntityAsync(entity, cancellationToken);
        }

        private string GenerateRowKey(SyncResultModel model)
        {
            // should generate a short unique id

            var uniqueId = $"{model.SyncType}-{model.CreatedUtc.Ticks}";
            var bytes = Encoding.UTF8.GetBytes(uniqueId);
            var hash = _hashAlgo.ComputeHash(bytes);
            var base64Hash = Convert.ToBase64String(hash);

            return base64Hash;
        }

        public async Task<List<SyncResultModel>> GetByBusiness(int businessAcctId, CancellationToken cancellationToken)
        {
            var tableEntities = TableClient
                .QueryAsync<SyncHistoryEntity>(f => f.PartitionKey == businessAcctId.ToString(), 1000, null, cancellationToken);

            var entities = new List<SyncHistoryEntity>();

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
            {
                entities.AddRange(page.Values);
            }

            entities = entities.OrderByDescending(e => e.CreatedUtc).Take(1000).ToList();

            return entities.ConvertAll(entity => entity.ToModel());
        }

        public async Task<List<SyncResultModel>> GetOldResults(DateTime cutoffDate, CancellationToken cancellationToken)
        {
            var tableEntities = TableClient
                .QueryAsync<SyncHistoryEntity>($"CreatedUtc lt datetime'{cutoffDate:yyyy-MM-dd}'", 1000, null, cancellationToken);

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
