using System;

namespace AplosConnector.Common.Models.Aplos
{
    public class AplosOutstandingBillModel
    {
        public string Id { get; set; }
        public string BillNumber { get; set; }
        public DateTime BillDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public int ContactId { get; set; }
        public string ContactName { get; set; }
        public string Note { get; set; }
    }
}
