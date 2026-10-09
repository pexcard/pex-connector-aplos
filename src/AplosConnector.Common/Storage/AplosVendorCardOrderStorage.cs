using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Storage;

// One row per connector-created vendor, written before its card is ordered: PEX card orders are not idempotent.
public class AplosVendorCardOrderStorage : AzureTableStorageAbstract, IAplosVendorCardOrderStorage
{
    public const string TABLE_NAME = "AplosVendorCardOrder";

    public AplosVendorCardOrderStorage(TableClient tableClient) : base(tableClient) { }

    public async Task<List<AplosVendorCardOrderModel>> GetByBusinessAsync(int pexBusinessAcctId, CancellationToken cancellationToken)
    {
        var partitionKey = pexBusinessAcctId.ToString();
        var tableEntities = TableClient
            .QueryAsync<AplosVendorCardOrderEntity>(entity => entity.PartitionKey == partitionKey, 1000, null, cancellationToken);

        List<AplosVendorCardOrderModel> models = [];

        await foreach (var page in tableEntities.AsPages().WithCancellation(cancellationToken))
        {
            foreach (var entity in page.Values)
            {
                models.Add(entity.ToModel());
            }
        }

        return models;
    }

    // Insert-only: an existing row means a card was already ordered for this vendor.
    public async Task AddAsync(AplosVendorCardOrderModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        await TableClient.AddEntityAsync(new AplosVendorCardOrderEntity(model), cancellationToken);
    }

    public async Task SetOrderIdAsync(AplosVendorCardOrderModel model, int cardOrderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var orderColumn = new TableEntity(model.PEXBusinessAcctId.ToString(), AplosVendorCardOrderEntity.GetRowKey(model))
        {
            [nameof(AplosVendorCardOrderEntity.CardOrderId)] = cardOrderId
        };

        await TableClient.UpdateEntityAsync(orderColumn, ETag.All, TableUpdateMode.Merge, cancellationToken);

        // Only after the write: the caller reports from the model, which must match what is stored.
        model.CardOrderId = cardOrderId;
    }

    public async Task MarkLinkedAsync(AplosVendorCardOrderModel model, int cardAcctId, DateTime linkedUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var linkedColumns = new TableEntity(model.PEXBusinessAcctId.ToString(), AplosVendorCardOrderEntity.GetRowKey(model))
        {
            [nameof(AplosVendorCardOrderEntity.CardAcctId)] = cardAcctId,
            [nameof(AplosVendorCardOrderEntity.LinkedUtc)] = linkedUtc.ToUniversalTime()
        };

        await TableClient.UpdateEntityAsync(linkedColumns, ETag.All, TableUpdateMode.Merge, cancellationToken);

        model.CardAcctId = cardAcctId;
        model.LinkedUtc = linkedUtc.ToUniversalTime();
    }
}
