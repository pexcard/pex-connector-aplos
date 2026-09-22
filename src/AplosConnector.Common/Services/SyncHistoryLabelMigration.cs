using AplosConnector.Common.Const;
using System;

namespace AplosConnector.Common.Services
{
    // Retirable in 2030 if sync history retention is still 3 years (TokenRefresher.cs:191) - by then no
    // pre-rename row can survive. Check that window before deleting.
    public static class SyncHistoryLabelMigration
    {
        // "Bill payments" meant PEX statement settlement before the AP stage took the name; the AP stage
        // writes "Bill Payments". The case is the discriminator, so Ordinal is deliberate.
        public static string DisplayLabel(string syncType) =>
            string.Equals(syncType, SyncTypes.LegacyBillPaymentsLabel, StringComparison.Ordinal)
                ? SyncTypes.PexStatementPayments
                : syncType;
    }
}
