using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using Microsoft.Azure.Cosmos.Table.Queryable;
using AplosConnector.Core.Storages;

namespace AplosConnector.Common.VendorCards
{
    public class VendorCardRepository : AzureTableStorageAbstract, IVendorCardRepository
    {
        private CloudTable _table;
        private const string _tableName = "VendorCardsCreated";

        private readonly IPexApiClient _pexApiClient;
        private readonly ILogger _logger;

        public VendorCardRepository(string connectionString, IPexApiClient pexApiClient, ILogger<VendorCardRepository> logger)
            : base(connectionString, _tableName, null)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
            }
            if (pexApiClient is null)
            {
                throw new ArgumentNullException(nameof(pexApiClient));
            }
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var tableClient = storageAccount.CreateCloudTableClient();

            tableClient.DefaultRequestOptions = new TableRequestOptions
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromMilliseconds(500), 3)
            };

            _table = tableClient.GetTableReference(_tableName);
            _pexApiClient = pexApiClient;
            _logger = logger;
        }

        public async Task<VendorCardsOrdered> GetVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, int orderId, CancellationToken cancelToken = default)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            var cardOrdersEntries = new List<VendorCardOrdered>();

            TableContinuationToken continuationToken = null;
            do
            {
                var segmentData = await _table.CreateQuery<VendorCardsOrderedEntity>()
                                  .Where(x => x.PartitionKey == mapping.PEXBusinessAcctId.ToString() && x.RowKey == orderId.ToString())
                                  .AsTableQuery()
                                  .ExecuteSegmentedAsync(continuationToken, cancelToken);
                foreach (var item in segmentData)
                {
                    var cardOrdersEntry = new VendorCardOrdered(item.OrderId, item.Id, item.Name, item.AutoFunding, item.InitialFunding, item.GroupId, orderDate: item.Created.UtcDateTime);
                    cardOrdersEntries.Add(cardOrdersEntry);
                }
            }
            while (continuationToken != null);

            var cardsOrderedData = await _pexApiClient.GetVendorCardOrder(mapping.PEXExternalAPIToken, orderId, cancelToken);

            var cardsOrdered = new VendorCardsOrdered(orderId, cardOrdersEntries);

            foreach (var cardOrdered in cardOrdersEntries)
            {
                var cardOrdersForVendorName = cardsOrderedData.Cards.Where(x => cardOrdered.Name.StartsWith(x.VendorName)).ToList();
                if (cardOrdersForVendorName.Count > 1)
                {
                    _logger.LogWarning($"{cardOrdersForVendorName.Count} cards in order {cardsOrderedData.CardOrderId} match (start-with) Aplos vendor name {cardOrdered.Name}.");
                }
                var cardOrderForVendorName = cardOrdersForVendorName.FirstOrDefault();

                cardOrdered.AccountId = cardOrderForVendorName?.AcctId;
                cardOrdered.Status = cardOrderForVendorName?.Status;
                cardOrdered.Error = cardOrderForVendorName?.ErrorMessage;
            }

            return cardsOrdered;
        }

        public async Task<List<VendorCardsOrdered>> GetAllVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, CancellationToken cancelToken = default)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            var cardOrdersEntries = new List<VendorCardOrdered>();

            TableContinuationToken continuationToken = null;
            do
            {
                var segmentData = await _table.CreateQuery<VendorCardsOrderedEntity>()
                                  .Where(x => x.PartitionKey == mapping.PEXBusinessAcctId.ToString())
                                  .AsTableQuery()
                                  .ExecuteSegmentedAsync(continuationToken, cancelToken);
                foreach (var item in segmentData)
                {
                    var cardOrdersEntry = new VendorCardOrdered(item.OrderId, item.Id, item.Name, item.AutoFunding, item.InitialFunding, item.GroupId, orderDate: item.Created.UtcDateTime);
                    cardOrdersEntries.Add(cardOrdersEntry);
                }
            }
            while (continuationToken != null);

            var cardsOrders = new List<VendorCardsOrdered>();

            var cardOrderGroups = cardOrdersEntries.GroupBy(x => x.OrderId).ToList();
            foreach (var cardOrderGroup in cardOrderGroups)
            {
                var cardsOrderedData = await _pexApiClient.GetVendorCardOrder(mapping.PEXExternalAPIToken, cardOrderGroup.Key, cancelToken);

                var cardsOrdered = new VendorCardsOrdered(cardOrderGroup.Key, cardOrderGroup);

                foreach (var cardOrdered in cardOrderGroup)
                {
                    var cardOrdersForVendorName = cardsOrderedData.Cards.Where(x => cardOrdered.Name.StartsWith(x.VendorName)).ToList();
                    if (cardOrdersForVendorName.Count > 1)
                    {
                        _logger.LogWarning($"{cardOrdersForVendorName.Count} cards in order {cardsOrderedData.CardOrderId} match (start-with) Aplos vendor name {cardOrdered.Name}.");
                    }
                    var cardOrderForVendorName = cardOrdersForVendorName.FirstOrDefault();

                    cardOrdered.AccountId = cardOrderForVendorName?.AcctId;
                    cardOrdered.Status = cardOrderForVendorName?.Status;
                    cardOrdered.Error = cardOrderForVendorName?.ErrorMessage;
                }

                cardsOrders.Add(cardsOrdered);
            }

            return cardsOrders;
        }

        public async Task SaveVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, VendorCardsOrdered vendorCardsOrdered, CancellationToken cancelToken = default)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }
            if (vendorCardsOrdered is null)
            {
                throw new ArgumentNullException(nameof(vendorCardsOrdered));
            }

            _logger.LogInformation("Saving {VendorCardsCount} vendor card order info for OrderId={VendorCardOrderId} to azure storage.", vendorCardsOrdered.CardOrders.Count, vendorCardsOrdered.Id);

            var aggregateExceptions = new List<Exception>();

            foreach (var vendorCardOrdered in vendorCardsOrdered.CardOrders)
            {
                try
                {
                    var vendorCardOrderEntity = new VendorCardsOrderedEntity
                    {
                        PartitionKey = mapping.PEXBusinessAcctId.ToString(),
                        RowKey = $"{vendorCardOrdered.OrderId}_{vendorCardOrdered.Id}",
                        ETag = "*",
                        OrderId = vendorCardOrdered.OrderId,
                        Id = vendorCardOrdered.Id,
                        Name = vendorCardOrdered.Name,
                        AutoFunding = vendorCardOrdered.AutoFunding,
                        InitialFunding = vendorCardOrdered.InitialFunding,
                        GroupId = vendorCardOrdered.GroupId,
                        Created = DateTimeOffset.UtcNow,
                    };

                    var operation = TableOperation.Insert(vendorCardOrderEntity);

                    await _table.ExecuteAsync(operation, null, null, cancelToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save vendor card oder. OrderId={VendorCardsOrderId}, VendorId={VendorCardsVendorId}, VendorName={VendorCardsVendorName}.", vendorCardOrdered.OrderId, vendorCardOrdered.Id, vendorCardOrdered.Name);
                    aggregateExceptions.Add(ex);
                }
            }

            if (aggregateExceptions.Any())
            {
                throw new AggregateException(aggregateExceptions);
            }
        }
    }
}
