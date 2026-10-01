using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Storage;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class AplosInvoiceOverpaymentTests
    {
        private const decimal RegisterAccount = 2000m;
        private const decimal CheckingAccount = 1000m;
        private const decimal RebateIncomeAccount = 4000m;

        private const string MissionsFundId = "60";
        private const string GeneralFundId = "30";
        private const string YouthFundId = "20";

        private readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
        private readonly Mock<IPexApiClient> _mockPexApiClient = new();
        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();

        private AplosApiTransactionDetail _createdTransaction;

        [Fact]
        public async Task RebateDistribute_AppliesTheRebateFirst_WhenTheBankTransferAlsoCoveredTheWholeInvoice()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 54.55m), (GeneralFundId, 27.27m), (YouthFundId, 18.18m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 5.45m), (GeneralFundId, 2.73m), (YouthFundId, 1.82m) });
        }

        [Fact]
        public async Task RebateDistribute_AppliesTheRebateFirst_WhenSingleFundInvoiceIsOverpaid()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 110.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 110.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 100.00m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 10.00m) });
        }

        [Fact]
        public async Task RebateDistribute_SyncsWithoutRebateSettings_WhenNoCreditPosts()
        {
            var mapping = NewMapping();
            mapping.PexRebatesAplosTransactionAccountNumber = decimal.Zero;
            mapping.PexRebatesAplosTaxTagId = null;

            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 115.00m) };

            var result = await SyncRebateDistribute(mapping, invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDistribute_AppliesTheRebateFirst_WhenAFeePostedAfterThePayment()
        {
            var invoice = NewInvoice(3002.39m);
            var allocations = ThreeFundAllocations(1800.00m, 900.00m, 302.39m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 3002.00m),
                NewPayment(PaymentType.RebateCredit, 59.78m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(2942.61m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 1800.00m), (GeneralFundId, 900.00m), (YouthFundId, 302.39m) });
            AssertBankCredits(new[] { (MissionsFundId, 1764.16m), (GeneralFundId, 882.08m), (YouthFundId, 296.37m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 35.84m), (GeneralFundId, 17.92m), (YouthFundId, 6.02m) });
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenRebateIsNeededAndRebateSettingsAreMissing()
        {
            var mapping = NewMapping();
            mapping.PexRebatesAplosTransactionAccountNumber = decimal.Zero;

            var invoice = NewInvoice(3002.39m);
            var allocations = ThreeFundAllocations(1800.00m, 900.00m, 302.39m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 3002.00m),
                NewPayment(PaymentType.RebateCredit, 59.78m),
            };

            var result = await SyncRebateDistribute(mapping, invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDistribute_WritesNoBankLine_WhenTheRebateCreditExceedsTheWholeInvoice()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.RebateCredit, 150.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(CheckingAccount));
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
        }

        [Fact]
        public async Task RebateDistribute_KeepsExistingBehaviour_WhenACarryOverCreditExactlyPaysPartOfTheInvoice()
        {
            var invoice = NewInvoice(500.00m);
            var allocations = ThreeFundAllocations(100.00m, 100.00m, 300.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.CarryOverCredit, 10.00m),
                NewPayment(PaymentType.PEXTransfer, 490.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(490.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 100.00m), (GeneralFundId, 100.00m), (YouthFundId, 300.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 98.00m), (GeneralFundId, 98.00m), (YouthFundId, 294.00m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 2.00m), (GeneralFundId, 2.00m), (YouthFundId, 6.00m) });
        }

        [Fact]
        public async Task RebateDistribute_KeepsExistingBehaviour_WhenTheInvoiceIsExactlyPaidByCash()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 110.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task EveryMethod_SkipsWithoutFailing_WhenTheInvoiceIsUnderpaid()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 100.00m) };

            Assert.Equal(TransactionSyncResult.NotEligible, await SyncRebateDistribute(NewMapping(), invoice, allocations, payments));
            Assert.Equal(TransactionSyncResult.NotEligible, await SyncSimple(invoice, allocations, payments));
            Assert.Equal(TransactionSyncResult.NotEligible, await SyncRebateDeposit(invoice, allocations, payments));
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDistribute_SyncsTheInvoiceAmountOnce_WhenCashAloneOverpaysIt()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 120.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(110.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDistribute_AppliesTheRebateFirst_WhenCashExceedsTheInvoiceAlongsideARebateCredit()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 115.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 54.55m), (GeneralFundId, 27.27m), (YouthFundId, 18.18m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 5.45m), (GeneralFundId, 2.73m), (YouthFundId, 1.82m) });
        }

        [Fact]
        public async Task RebateDistribute_SyncsTheInvoiceAmountOnce_WhenCashOverpaysAndACreditIsReversed()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.SameDayACH, 110.00m),
                NewPayment(PaymentType.RebateCreditReversal, 10.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 100.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 100.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDeposit_SubtractsReversalsFromCash()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.SameDayACH, 100.00m),
                NewPayment(PaymentType.Reversal, 40.00m),
                NewPayment(PaymentType.SameDayACH, 40.00m),
            };

            var result = await SyncRebateDeposit(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertBankCredits(new[] { (MissionsFundId, 100.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDeposit_SyncsTheInvoiceAmountOnce_WhenCashOverpaysAndACreditIsReversed()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.SameDayACH, 110.00m),
                NewPayment(PaymentType.WriteOffReversal, 10.00m),
            };

            var result = await SyncRebateDeposit(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 100.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 100.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task RebateDeposit_PostsOnlyTheCredit_WhenNetCashIsNegative()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.Reversal, 10.00m),
                NewPayment(PaymentType.RebateCredit, 110.00m),
            };

            var result = await SyncRebateDeposit(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 100.00m) });
            AssertRebateIncomeCredits(new[] { ("60", 100.00m) });
            Assert.Equal(new[] { -100.00m, 100.00m }, LinesFor(CheckingAccount).Select(line => line.Amount));
        }

        [Fact]
        public async Task RebateDistribute_PostsOnlyTheCredit_WhenNetCashIsNegative()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.Reversal, 10.00m),
                NewPayment(PaymentType.RebateCredit, 110.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 100.00m) });
            Assert.Empty(LinesFor(CheckingAccount));
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 100.00m) });
        }

        [Theory]
        [InlineData(PaymentType.WriteOff)]
        [InlineData(PaymentType.SalesCredit)]
        public async Task RebateDistribute_TreatsWriteOffsAndSalesCreditsAsCredits(PaymentType creditType)
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 99.00m),
                NewPayment(creditType, 11.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(99.00m, _createdTransaction.Amount);
            AssertBankCredits(new[] { (MissionsFundId, 54.00m), (GeneralFundId, 27.00m), (YouthFundId, 18.00m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 6.00m), (GeneralFundId, 3.00m), (YouthFundId, 2.00m) });
        }

        [Fact]
        public async Task RebateDistribute_SubtractsWriteOffReversalsFromCredits()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 99.00m),
                NewPayment(PaymentType.WriteOff, 22.00m),
                NewPayment(PaymentType.WriteOffReversal, 11.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(99.00m, _createdTransaction.Amount);
            AssertBankCredits(new[] { (MissionsFundId, 54.00m), (GeneralFundId, 27.00m), (YouthFundId, 18.00m) });
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 6.00m), (GeneralFundId, 3.00m), (YouthFundId, 2.00m) });
        }

        [Fact]
        public async Task RebateDistribute_SkipsWithoutFailing_WhenAWriteOffReversalLeavesAShortfall()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 99.00m),
                NewPayment(PaymentType.WriteOff, 11.00m),
                NewPayment(PaymentType.WriteOffReversal, 11.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.NotEligible, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task RebateDistribute_SubtractsReversalsFromCash()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.SameDayACH, 100.00m),
                NewPayment(PaymentType.Reversal, 40.00m),
                NewPayment(PaymentType.SameDayACH, 40.00m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertBankCredits(new[] { (MissionsFundId, 100.00m) });
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task Simple_SubtractsReversalsFromCash()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.SameDayACH, 100.00m),
                NewPayment(PaymentType.Reversal, 40.00m),
                NewPayment(PaymentType.SameDayACH, 40.00m),
            };

            var service = GetAplosIntegrationService();
#pragma warning disable CS0618
            var result = await service.SyncInvoiceSimple(
                NewMapping(), invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);
#pragma warning restore CS0618

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            Assert.Equal(new[] { -100.00m }, LinesFor(CheckingAccount).Select(line => line.Amount));
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task Simple_SyncsTheInvoiceAmountOnce_WhenCashOverpaysAndACreditIsReversed()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.WriteOffReversal, 10.00m),
            };

            var result = await SyncSimple(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            Assert.Equal(new[] { 100.00m }, LinesFor(RegisterAccount).Select(line => line.Amount));
            Assert.Equal(new[] { -100.00m }, LinesFor(CheckingAccount).Select(line => line.Amount));
            Assert.Empty(LinesFor(RebateIncomeAccount));
        }

        [Fact]
        public async Task Simple_PostsOnlyTheCredit_WhenNetCashIsNegative()
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = new[]
            {
                NewPayment(PaymentType.Reversal, 10.00m),
                NewPayment(PaymentType.RebateCredit, 110.00m),
            };

            var result = await SyncSimple(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            Assert.Equal(new[] { 100.00m }, LinesFor(RegisterAccount).Select(line => line.Amount));
            Assert.Empty(LinesFor(CheckingAccount));
            AssertRebateIncomeCredits(new[] { ("60", 100.00m) });
        }

        [Fact]
        public async Task Simple_WritesNoCashLine_WhenCreditsCoverTheWholeInvoice()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.WriteOff, 110.00m) };

            var result = await SyncSimple(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            Assert.Empty(LinesFor(CheckingAccount));
            AssertRebateIncomeCredits(new[] { ("60", 110.00m) });
        }

        [Fact]
        public async Task RebateDistribute_WritesNoBankLine_WhenCreditsExactlyCoverTheWholeInvoice()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[] { NewPayment(PaymentType.WriteOff, 110.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(0m, _createdTransaction.Amount);
            Assert.Empty(LinesFor(CheckingAccount));
            AssertRebateIncomeCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
        }

        [Fact]
        public async Task RebateDeposit_PostsWriteOffsToTheRebateFund()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 99.00m),
                NewPayment(PaymentType.WriteOff, 11.00m),
            };

            var service = GetAplosIntegrationService();
            var result = await service.SyncInvoiceRebateDeposit(
                NewMapping(), invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(99.00m, _createdTransaction.Amount);
            AssertRebateIncomeCredits(new[] { ("60", 11.00m) });
        }

        [Theory]
        [InlineData(PaymentType.PEXTransfer, false, 10.00)]
        [InlineData(PaymentType.SameDayACH, false, 10.00)]
        [InlineData(PaymentType.Reversal, false, -10.00)]
        [InlineData(PaymentType.SalesCredit, true, 10.00)]
        [InlineData(PaymentType.WriteOff, true, 10.00)]
        [InlineData(PaymentType.RebateCredit, true, 10.00)]
        [InlineData(PaymentType.CarryOverCredit, true, 10.00)]
        [InlineData(PaymentType.RebateCreditReversal, true, -10.00)]
        [InlineData(PaymentType.WriteOffReversal, true, -10.00)]
        public void ClassifyInvoicePayment_SplitsCashFromCredits(PaymentType type, bool expectedIsCredit, decimal expectedAmount)
        {
            var classification = AplosIntegrationService.ClassifyInvoicePayment(NewPayment(type, 10.00m));

            Assert.Equal((expectedIsCredit, expectedAmount), classification);
        }

        [Fact]
        public void ClassifyInvoicePayment_ReturnsNull_ForAnUnknownType()
        {
            Assert.Null(AplosIntegrationService.ClassifyInvoicePayment(NewPayment((PaymentType)99, 10.00m)));
        }

        [Fact]
        public void DistributeInvoicePayments_DoesNotDivideByZero_WhenAllocationsAreEmptyOfValue()
        {
            var allocations = new[] { (1, 0m), (2, 0m) };

            var splits = AplosIntegrationService.DistributeInvoicePayments(allocations, 0m, 10.00m);

            Assert.All(splits, split => Assert.Equal(0m, split.RebateIncomeAmount));
            Assert.All(splits, split => Assert.Equal(0m, split.BankAmount));
            Assert.All(splits, split => Assert.Equal(0m, split.RegisterAmount));
        }

        [Fact]
        public async Task RebateDistribute_Fails_WhenAllocationsDoNotAddUpToTheInvoiceAmount()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = new[]
            {
                NewAllocation(MissionsFundId, 60.00m),
                NewAllocation("999", 50.00m),
            };
            var payments = new[] { NewPayment(PaymentType.PEXTransfer, 110.00m) };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Failed, result);
            Assert.Null(_createdTransaction);
        }

        [Fact]
        public async Task Simple_AppliesTheRebateFirst_WhenTheInvoiceIsOverpaid()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncSimple(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            Assert.Equal(new[] { 10.00m, 100.00m }, LinesFor(RegisterAccount).Select(line => line.Amount));
            Assert.Equal(new[] { -100.00m }, LinesFor(CheckingAccount).Select(line => line.Amount));
            AssertRebateIncomeCredits(new[] { ("60", 10.00m) });
        }

        [Fact]
        public async Task RebateDeposit_AppliesTheRebateFirst_WhenTheInvoiceIsOverpaid()
        {
            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 110.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            var result = await SyncRebateDeposit(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(100.00m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m) });
            AssertBankCredits(new[] { (MissionsFundId, 60.00m), (GeneralFundId, 30.00m), (YouthFundId, 20.00m), ("60", -10.00m) });
            AssertRebateIncomeCredits(new[] { ("60", 10.00m) });
        }

        [Fact]
        public void OverpaidInvoiceAlreadyInAplos_IsNotSyncedASecondTime()
        {
            var service = GetAplosIntegrationService();
            var aplosTransactions = new[] { new AplosApiTransactionDetail { Note = "94460" } };

            Assert.True(service.WasPexTransactionSyncedToAplos(aplosTransactions, "94460"));
            Assert.False(service.WasPexTransactionSyncedToAplos(aplosTransactions, "94461"));
        }

        [Theory]
        [InlineData(110.00, 110.00, true)]
        [InlineData(110.00, 120.00, true)]
        [InlineData(110.00, 109.99, false)]
        [InlineData(110.00, 0.00, false)]
        public void IsInvoiceFullyPaid_AcceptsPaymentsAtOrAboveTheInvoiceAmount(
            decimal invoiceAmount, decimal totalPaymentsAmount, bool expected)
        {
            Assert.Equal(expected, AplosIntegrationService.IsInvoiceFullyPaid(invoiceAmount, totalPaymentsAmount));
        }

        [Theory]
        [InlineData(1, 100.00, 0.00)]
        [InlineData(2, 90.00, 10.00)]
        [InlineData(3, 90.00, 10.00)]
        [InlineData(4, 0.00, 100.00)]
        [InlineData(5, 100.00, 0.00)]
        public void SplitInvoicePaymentTotals_AppliesCreditsFirstUpToTheInvoiceAmount(int example, decimal expectedBankAmount, decimal expectedCreditAmount)
        {
            var (bankAmount, creditAmount) = AplosIntegrationService.SplitInvoicePaymentTotals(100.00m, CreditsFirstExamplePayments(example));

            Assert.Equal(expectedBankAmount, bankAmount);
            Assert.Equal(expectedCreditAmount, creditAmount);
        }

        [Theory]
        [InlineData(1, 100.00, 0.00)]
        [InlineData(2, 90.00, 10.00)]
        [InlineData(3, 90.00, 10.00)]
        [InlineData(4, 0.00, 100.00)]
        [InlineData(5, 100.00, 0.00)]
        public async Task EveryMethod_PostsTheInvoiceAmountWithCreditsFirst_ForAHundredDollarInvoice(int example, decimal expectedBankAmount, decimal expectedCreditAmount)
        {
            var invoice = NewInvoice(100.00m);
            var allocations = new[] { NewAllocation(MissionsFundId, 100.00m) };
            var payments = CreditsFirstExamplePayments(example);

            foreach (var sync in new Func<Task<TransactionSyncResult>>[]
            {
                () => SyncRebateDistribute(NewMapping(), invoice, allocations, payments),
                () => SyncSimple(invoice, allocations, payments),
                () => SyncRebateDeposit(invoice, allocations, payments),
            })
            {
                _createdTransaction = null;

                Assert.Equal(TransactionSyncResult.Success, await sync());
                Assert.Equal(expectedBankAmount, _createdTransaction.Amount);
                Assert.Equal(100.00m, LinesFor(RegisterAccount).Sum(line => line.Amount));
                Assert.Equal(-expectedBankAmount, LinesFor(CheckingAccount).Sum(line => line.Amount));
                Assert.Equal(-expectedCreditAmount, LinesFor(RebateIncomeAccount).Sum(line => line.Amount));
                Assert.DoesNotContain(_createdTransaction.Lines, line => line.Amount == 0);
                Assert.All(LinesFor(RegisterAccount), line => Assert.True(line.Amount > 0));
                Assert.All(LinesFor(RebateIncomeAccount), line => Assert.True(line.Amount < 0));
            }
        }

        [Fact]
        public void DistributeInvoicePayments_PutsTheRoundingRemainderOnTheLastFund()
        {
            var allocations = new[] { (1, 1800.00m), (2, 900.00m), (3, 302.39m) };

            var splits = AplosIntegrationService.DistributeInvoicePayments(allocations, 3002.39m, 59.78m);

            Assert.Equal(new[] { 35.84m, 17.92m, 6.02m }, splits.Select(s => s.RebateIncomeAmount));
            Assert.Equal(new[] { 1764.16m, 882.08m, 296.37m }, splits.Select(s => s.BankAmount));
            Assert.Equal(new[] { 1800.00m, 900.00m, 302.39m }, splits.Select(s => s.RegisterAmount));
        }

        [Fact]
        public void DistributeInvoicePayments_LeavesEveryFundOnCash_WhenThereAreNoCredits()
        {
            var allocations = new[] { (1, 60.00m), (2, 30.00m), (3, 20.00m) };

            var splits = AplosIntegrationService.DistributeInvoicePayments(allocations, 110.00m, 0m);

            Assert.All(splits, split => Assert.Equal(0m, split.RebateIncomeAmount));
            Assert.Equal(new[] { 60.00m, 30.00m, 20.00m }, splits.Select(s => s.BankAmount));
            Assert.Equal(new[] { 60.00m, 30.00m, 20.00m }, splits.Select(s => s.RegisterAmount));
        }

        [Fact]
        public async Task RebateDistribute_DebitsEachFundsRegisterWithItsAllocation_WhenTheSharesRoundOnAHalfCent()
        {
            var invoice = NewInvoice(2.02m);
            var allocations = new[] { NewAllocation(MissionsFundId, 1.01m), NewAllocation(GeneralFundId, 1.01m) };
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 2.01m),
                NewPayment(PaymentType.RebateCredit, 0.01m),
            };

            var result = await SyncRebateDistribute(NewMapping(), invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(2.01m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 1.01m), (GeneralFundId, 1.01m) });
            AssertBankCredits(new[] { (MissionsFundId, 1.01m), (GeneralFundId, 1.00m) });
            AssertRebateIncomeCredits(new[] { (GeneralFundId, 0.01m) });
        }

        [Fact]
        public async Task RebateDeposit_DebitsEachFundsRegisterWithItsAllocation_WhenTheSharesRoundOnAHalfCent()
        {
            var invoice = NewInvoice(2.02m);
            var allocations = new[] { NewAllocation(MissionsFundId, 1.01m), NewAllocation(GeneralFundId, 1.01m) };
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 2.01m),
                NewPayment(PaymentType.RebateCredit, 0.01m),
            };

            var result = await SyncRebateDeposit(invoice, allocations, payments);

            Assert.Equal(TransactionSyncResult.Success, result);
            Assert.Equal(2.01m, _createdTransaction.Amount);
            AssertRegisterDebits(new[] { (MissionsFundId, 1.01m), (GeneralFundId, 1.01m) });
            AssertRebateIncomeCredits(new[] { ("60", 0.01m) });
        }

        [Fact]
        public async Task SimpleAndRebateDeposit_Fail_WhenACreditPostsAndTheRebateAccountIsMissing()
        {
            var mapping = NewMapping();
            mapping.PexRebatesAplosTransactionAccountNumber = decimal.Zero;

            var invoice = NewInvoice(110.00m);
            var allocations = ThreeFundAllocations(60.00m, 30.00m, 20.00m);
            var payments = new[]
            {
                NewPayment(PaymentType.PEXTransfer, 100.00m),
                NewPayment(PaymentType.RebateCredit, 10.00m),
            };

            Assert.Equal(TransactionSyncResult.Failed, await SyncSimple(invoice, allocations, payments, mapping));
            Assert.Null(_createdTransaction);
            Assert.Equal(TransactionSyncResult.Failed, await SyncRebateDeposit(invoice, allocations, payments, mapping));
            Assert.Null(_createdTransaction);
        }

        private static InvoicePaymentModel[] CreditsFirstExamplePayments(int example) => example switch
        {
            1 => [NewPayment(PaymentType.PEXTransfer, 110.00m)],
            2 => [NewPayment(PaymentType.PEXTransfer, 100.00m), NewPayment(PaymentType.RebateCredit, 10.00m)],
            3 => [NewPayment(PaymentType.CarryOverCredit, 10.00m), NewPayment(PaymentType.PEXTransfer, 90.00m)],
            4 => [NewPayment(PaymentType.Reversal, 10.00m), NewPayment(PaymentType.RebateCredit, 110.00m)],
            5 => [NewPayment(PaymentType.PEXTransfer, 110.00m), NewPayment(PaymentType.RebateCreditReversal, 10.00m)],
            _ => throw new ArgumentOutOfRangeException(nameof(example)),
        };

        private async Task<TransactionSyncResult> SyncRebateDistribute(
            Pex2AplosMappingModel mapping,
            InvoiceModel invoice,
            IReadOnlyList<InvoiceAllocationModel> allocations,
            IReadOnlyList<InvoicePaymentModel> payments)
        {
            var service = GetAplosIntegrationService();

            return await service.SyncInvoiceRebateDistribute(
                mapping, invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);
        }

        private async Task<TransactionSyncResult> SyncRebateDeposit(
            InvoiceModel invoice,
            IReadOnlyList<InvoiceAllocationModel> allocations,
            IReadOnlyList<InvoicePaymentModel> payments,
            Pex2AplosMappingModel mapping = null)
        {
            var service = GetAplosIntegrationService();

            return await service.SyncInvoiceRebateDeposit(
                mapping ?? NewMapping(), invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);
        }

        private async Task<TransactionSyncResult> SyncSimple(
            InvoiceModel invoice,
            IReadOnlyList<InvoiceAllocationModel> allocations,
            IReadOnlyList<InvoicePaymentModel> payments,
            Pex2AplosMappingModel mapping = null)
        {
            var service = GetAplosIntegrationService();

#pragma warning disable CS0618
            return await service.SyncInvoiceSimple(
                mapping ?? NewMapping(), invoice, allocations, payments, AplosFunds(), NullLogger.Instance, default);
#pragma warning restore CS0618
        }

        private void AssertRegisterDebits((string fundId, decimal amount)[] expected) =>
            AssertLines(RegisterAccount, expected.Select(e => (e.fundId, e.amount)).ToArray());

        private void AssertBankCredits((string fundId, decimal amount)[] expected) =>
            AssertLines(CheckingAccount, expected.Select(e => (e.fundId, -e.amount)).ToArray());

        private void AssertRebateIncomeCredits((string fundId, decimal amount)[] expected) =>
            AssertLines(RebateIncomeAccount, expected.Select(e => (e.fundId, -e.amount)).ToArray());

        private void AssertLines(decimal accountNumber, (string fundId, decimal amount)[] expected)
        {
            var actual = LinesFor(accountNumber)
                .Select(line => (fundId: line.Fund.Id.ToString(), amount: line.Amount))
                .ToArray();

            Assert.Equal(expected, actual);
        }

        private AplosApiTransactionLineDetail[] LinesFor(decimal accountNumber)
        {
            Assert.NotNull(_createdTransaction);

            return _createdTransaction.Lines
                .Where(line => line.Account.AccountNumber == accountNumber)
                .ToArray();
        }

        private static InvoiceModel NewInvoice(decimal invoiceAmount) => new()
        {
            InvoiceId = 94460,
            InvoiceAmount = invoiceAmount,
            Status = InvoiceStatus.Closed,
            DueDate = new DateTime(2026, 8, 1),
        };

        private static InvoicePaymentModel NewPayment(PaymentType type, decimal amount) => new()
        {
            Type = type,
            Amount = amount,
            DatePaid = new DateTime(2026, 8, 1),
        };

        private static InvoiceAllocationModel NewAllocation(string aplosFundId, decimal totalAmount) => new()
        {
            InvoiceId = 94460,
            TagValue = aplosFundId,
            TotalAmount = totalAmount,
        };

        private static InvoiceAllocationModel[] ThreeFundAllocations(
            decimal missionsAmount, decimal generalAmount, decimal youthAmount) =>
        [
            NewAllocation(MissionsFundId, missionsAmount),
            NewAllocation(GeneralFundId, generalAmount),
            NewAllocation(YouthFundId, youthAmount),
        ];

        private static List<PexAplosApiObject> AplosFunds() =>
        [
            new PexAplosApiObject { Id = MissionsFundId, Name = "Missions" },
            new PexAplosApiObject { Id = GeneralFundId, Name = "General" },
            new PexAplosApiObject { Id = YouthFundId, Name = "Youth" },
        ];

        private static Pex2AplosMappingModel NewMapping() => new()
        {
            PEXBusinessAcctId = 6118231,
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            SyncInvoices = true,
            SyncInvoicesMethod = "rebate-distribute",
            SyncInvoiceAggregated = false,
            AplosRegisterAccountNumber = RegisterAccount,
            TransfersAplosTransactionAccountNumber = CheckingAccount,
            TransfersAplosContactId = 777,
            PexRebatesAplosTransactionAccountNumber = RebateIncomeAccount,
            PexRebatesAplosFundId = 60,
            PexRebatesAplosTaxTagId = "tax-1",
        };

        private AplosIntegrationService GetAplosIntegrationService()
        {
            _mockOptions.Setup(options => options.Value).Returns(new AppSettingsModel());

            _mockAplosApiClient
                .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
                .Callback<AplosApiTransactionDetail, CancellationToken>((transaction, _) => _createdTransaction = transaction)
                .Returns(Task.FromResult(new AplosApiTransactionResponse()));

            _mockAplosApiClientFactory
                .Setup(factory => factory.CreateClient(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Uri>(),
                    It.IsAny<Func<ILogger, AplosAuthModel>>(),
                    It.IsAny<Func<AplosAuthModel, ILogger, CancellationToken, Task>>()))
                .Returns(_mockAplosApiClient.Object);

            return new AplosIntegrationService(
                new NullLogger<AplosIntegrationService>(),
                _mockOptions.Object,
                _mockAplosApiClientFactory.Object,
                _mockAplosIntegrationMappingService.Object,
                _mockPexApiClient.Object,
                null,
                null,
                new SyncSettingsModel(),
                null,
                Mock.Of<IAplosBillMappingStorage>());
        }
    }
}
