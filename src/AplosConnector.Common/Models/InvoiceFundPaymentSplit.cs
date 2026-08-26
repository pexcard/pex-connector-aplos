namespace AplosConnector.Common.Models
{
    public sealed record InvoiceFundPaymentSplit(
        int AplosFundId,
        decimal AllocationAmount,
        decimal BankAmount,
        decimal RebateIncomeAmount);
}
