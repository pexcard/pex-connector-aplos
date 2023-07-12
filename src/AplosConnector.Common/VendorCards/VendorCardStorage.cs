using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using AplosConnector.Common.Entities;
using AplosConnector.Common.Models;
using AplosConnector.Common.Storage;
using Azure;
using Azure.Data.Tables;

namespace AplosConnector.Common.VendorCards
{
    public class VendorCardStorage : AzureTableStorageAbstract, IVendorCardStorage
    {
        public const string TABLE_NAME = "VendorCardsCreated";

        private readonly IPexApiClient _pexApiClient;
        private readonly ILogger _logger;

        public VendorCardStorage(TableClient tableClient, IPexApiClient pexApiClient,
            ILogger<VendorCardStorage> logger) : base(tableClient)
        {
            _pexApiClient = pexApiClient ?? throw new ArgumentNullException(nameof(pexApiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<VendorCardsOrdered> GetVendorCardsOrderedAsync(Pex2AplosMappingModel mapping, int orderId, CancellationToken cancelToken = default)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            var cardOrdersEntries = new List<VendorCardOrdered>();

            var tableEntities = TableClient.QueryAsync<VendorCardsOrderedEntity>(
                x => x.PartitionKey == mapping.PEXBusinessAcctId.ToString() && x.RowKey == orderId.ToString());

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancelToken))
            {
                foreach (var item in page.Values)
                {
                    var cardOrdersEntry = new VendorCardOrdered(item.OrderId, item.Id, item.Name, item.AutoFunding,
                        item.InitialFunding, item.GroupId, orderDate: item.Created.UtcDateTime);
                    cardOrdersEntries.Add(cardOrdersEntry);
                }
            }

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

            var tableEntities = TableClient.QueryAsync<VendorCardsOrderedEntity>(
                x => x.PartitionKey == mapping.PEXBusinessAcctId.ToString());

            await foreach (var page in tableEntities.AsPages().WithCancellation(cancelToken))
            {
                foreach (var item in page.Values)
                {
                    var cardOrdersEntry = new VendorCardOrdered(item.OrderId, item.Id, item.Name, item.AutoFunding,
                        item.InitialFunding, item.GroupId, orderDate: item.Created.UtcDateTime);
                    cardOrdersEntries.Add(cardOrdersEntry);
                }
            }

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
                        ETag = new ETag(),
                        OrderId = vendorCardOrdered.OrderId,
                        Id = vendorCardOrdered.Id,
                        Name = vendorCardOrdered.Name,
                        AutoFunding = vendorCardOrdered.AutoFunding,
                        InitialFunding = vendorCardOrdered.InitialFunding,
                        GroupId = vendorCardOrdered.GroupId,
                        Created = DateTimeOffset.UtcNow,
                    };

                    await TableClient.AddEntityAsync(vendorCardOrderEntity, cancelToken);
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
