using System;
using Microsoft.Azure.Cosmos.Table;

namespace AplosConnector.Common.Entities
{
    public class VendorCardsOrderedEntity : TableEntity
    {
        public int OrderId { get; set; }

        public string Id { get; set; }

        public string Name { get; set; }

        public bool AutoFunding { get; set; }

        public double? InitialFunding { get; set; }

        public int? GroupId { get; internal set; }

        public DateTimeOffset Created { get; set; }
    }
}
