namespace AplosConnector.Common.Models
{
    public class ExpenseAccountMappingModel
    {
        public bool SyncExpenseAccounts { get; set; }
        public string ExpenseAccountsPexTagId { get; set; }
        public decimal DefaultAplosTransactionAccountNumber { get; set; }
    }
}
