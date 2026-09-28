using AplosConnector.Common.Const;
using AplosConnector.Common.Services;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class SyncHistoryLabelMigrationTests
    {
        [Fact]
        public void ALegacyRowIsShownUnderTheNameTheStageNowUses()
        {
            Assert.Equal(SyncTypes.PexStatementPayments, SyncHistoryLabelMigration.DisplayLabel("Bill payments"));
        }

        // An ignore-case comparison here would relabel every genuine AP row.
        [Fact]
        public void ARealApRowIsNotRelabelled()
        {
            Assert.Equal(SyncTypes.BillPayments, SyncHistoryLabelMigration.DisplayLabel(SyncTypes.BillPayments));
        }

        [Theory]
        [InlineData("BILL PAYMENTS")]
        [InlineData("bill payments")]
        [InlineData("Bill Payments")]
        public void OnlyTheExactLegacyCasingIsTreatedAsLegacy(string syncType)
        {
            Assert.Equal(syncType, SyncHistoryLabelMigration.DisplayLabel(syncType));
        }

        [Theory]
        [InlineData(SyncTypes.Transactions)]
        [InlineData(SyncTypes.Reimbursements)]
        [InlineData(SyncTypes.PexStatementPayments)]
        [InlineData(SyncTypes.OutstandingBills)]
        public void NoOtherLabelIsEverRewritten(string syncType)
        {
            Assert.Equal(syncType, SyncHistoryLabelMigration.DisplayLabel(syncType));
        }

        [Fact]
        public void TheTwoLabelsDifferOrNothingCanBeToldApart()
        {
            Assert.NotEqual(SyncTypes.BillPayments, SyncTypes.LegacyBillPaymentsLabel);
            Assert.Equal("Bill payments", SyncTypes.LegacyBillPaymentsLabel);
            Assert.Equal("Bill Payments", SyncTypes.BillPayments);
        }
    }
}
