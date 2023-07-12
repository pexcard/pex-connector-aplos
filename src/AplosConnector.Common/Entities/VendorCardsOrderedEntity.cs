using System;
using Azure;
using Azure.Data.Tables;

namespace AplosConnector.Common.Entities
{
    public class VendorCardsOrderedEntity : ITableEntity
    {
        public int OrderId { get; set; }

        public string Id { get; set; }

        public string Name { get; set; }

        public bool AutoFunding { get; set; }

        public double? InitialFunding { get; set; }

        public int? GroupId { get; internal set; }

        public DateTimeOffset Created { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
