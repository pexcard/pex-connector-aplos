using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Storage
{
    public class AplosBillMappingStorage : AzureTableStorageAbstract, IAplosBillMappingStorage
    {
        public const string TABLE_NAME = "AplosBillMapping";

        public AplosBillMappingStorage(TableClient tableClient) : base(tableClient) { }

        public async Task<List<AplosBillMappingModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
        {
            var partitionKey = pexBusinessAcctId.ToString();
            var tableEntities = TableClient
                .QueryAsync<AplosBillMappingEntity>(entity => entity.PartitionKey == partitionKey, 1000, null, cancellationToken);

            var models = new List<AplosBillMappingModel>();

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
            {
                foreach (var entity in page.Values)
                {
                    models.Add(entity.ToModel());
                }
            }

            return models;
        }

        // Insert-only: a row that already exists means another run imported the same payable, and replacing it
        // would orphan that run's bill inbox item. The caller sees the 409.
        public async Task AddAsync(AplosBillMappingModel model, CancellationToken cancellationToken)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrEmpty(model.AplosPayableId)) throw new ArgumentException("An Aplos payable id is required.", nameof(model));

            await TableClient.AddEntityAsync(new AplosBillMappingEntity(model), cancellationToken);
        }

        // Update, never upsert: marking paid must not create a row for a bill that was never imported.
        // Merges only the paid column, so a stale model cannot overwrite the rest of the row.
        public async Task MarkPaidAsync(AplosBillMappingModel model, DateTime paidUtc, CancellationToken cancellationToken)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            model.PaidSyncedUtc = paidUtc.ToUniversalTime();

            var paidColumn = new TableEntity(model.PEXBusinessAcctId.ToString(), model.AplosPayableId)
            {
                [nameof(AplosBillMappingEntity.PaidSyncedUtc)] = model.PaidSyncedUtc
            };

            await TableClient.UpdateEntityAsync(paidColumn, ETag.All, TableUpdateMode.Merge, cancellationToken);
        }

        // Same shape as MarkPaidAsync: update-only, and only the first-failure column changes.
        public async Task MarkFailedAsync(AplosBillMappingModel model, DateTime failedUtc, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(model);

            model.FirstFailedUtc = failedUtc.ToUniversalTime();

            var failedColumn = new TableEntity(model.PEXBusinessAcctId.ToString(), model.AplosPayableId)
            {
                [nameof(AplosBillMappingEntity.FirstFailedUtc)] = model.FirstFailedUtc
            };

            await TableClient.UpdateEntityAsync(failedColumn, ETag.All, TableUpdateMode.Merge, cancellationToken);
        }
    }
}
