namespace AplosConnector.Common.Models
{
    public class AplosTransferModel
    {
        public string Id { get; set; }
        public decimal Amount { get; set; }
        public string FromAccountId { get; set; }
        public string ToAccountId { get; set; }
        public string PrivateNote { get; set; }
    }
}
