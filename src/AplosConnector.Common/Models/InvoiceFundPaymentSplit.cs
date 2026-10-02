namespace AplosConnector.Common.Models
{
    public sealed record InvoiceFundPaymentSplit(
        int AplosFundId,
        decimal RegisterAmount,
        decimal BankAmount,
        decimal RebateIncomeAmount);
}
