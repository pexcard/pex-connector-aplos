using System.Collections.Generic;

namespace AplosConnector.Common.Models
{
    public class PexTagValuesModel
    {
        public int AplosContactId { get; set; }
        public int AplosFundId { get; set; }
        public decimal AplosTransactionAccountNumber { get; set; }
        public decimal AplosRegisterAccountNumber { get; set; }
        public List<string> AplosTagIds { get; set; }
        public string AplosTaxTagId { get; set; }
    }
}