namespace AplosConnector.Common.Const
{
    public static class SyncTypes
    {
        public const string TagValuesFunds = "Tag Values (Funds)";
        public const string TagValuesAccounts = "Tag Values (Accounts)";
        public const string Transactions = "Transactions";
        public const string Rebates = "Rebates";
        public const string PexStatementPayments = "PEX Statement Payments";
        public const string Transfers = "Transfers";
        public const string PexAccountFees = "PEX Account Fees";
        public const string Reimbursements = "Reimbursements";

        // Title case, unlike LegacyBillPaymentsLabel. The difference is load-bearing, not a typo.
        public const string BillPayments = "Bill Payments";

        // What SyncInvoices wrote before the rename. Describes data already written - never change it.
        public const string LegacyBillPaymentsLabel = "Bill payments";
    }
}
