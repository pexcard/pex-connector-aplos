using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using Moq;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Tests;

public abstract class PexBillPaymentsTestBase : BillPaymentsTestBase
{
    protected const int FundId = 225;
    protected const int OtherFundId = 226;
    protected const decimal PurchasesAccountNumber = 6000m;
    protected const decimal NoTagsAccountNumber = 5003m;
    protected const decimal TaggedAccountNumber = 5004m;
    protected const string FundTagId = "fund-tag";
    protected const string PurchasesAccountTagId = "purchases-account-tag";
    protected const string PayeeName = "Acme Supplies";

    protected readonly List<AplosApiTransactionDetail> _postedTransactions = [];
    protected readonly List<AplosApiTransactionDetail> _aplosTransactions = [];
    protected readonly List<List<string>> _cardNotesAtPost = [];

    private readonly List<(BillPaymentModel Bill, BillPaymentRequestModel Request)> _bills = [];

    protected PexBillPaymentsTestBase()
    {
        _mockAplosApiClient
            .Setup(client => client.GetFunds(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockAplosApiClient
            .Setup(client => client.GetAccounts(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockAplosApiClient
            .Setup(client => client.GetTags(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _mockAplosApiClient
            .Setup(client => client.GetTransactions(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. _aplosTransactions]);
        _mockAplosApiClient
            .Setup(client => client.CreateTransaction(It.IsAny<AplosApiTransactionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<AplosApiTransactionDetail, CancellationToken>((transaction, _) =>
            {
                _postedTransactions.Add(transaction);
                _cardNotesAtPost.Add([.. _cardTransactionNotes]);
            })
            .ReturnsAsync(() => new AplosApiTransactionResponse
            {
                Data = new AplosApiTransactionData { Transaction = new AplosApiTransactionDetail { Id = 77000 } }
            });

        _mockAplosIntegrationMappingService
            .Setup(service => service.Map(It.IsAny<IEnumerable<AplosApiFundDetail>>()))
            .Returns([new PexAplosApiObject { Id = FundId.ToString(), Name = "General Fund" }, new PexAplosApiObject { Id = OtherFundId.ToString(), Name = "Building Fund" }]);
        _mockAplosIntegrationMappingService
            .Setup(service => service.Map(It.IsAny<IEnumerable<AplosApiAccountDetail>>()))
            .Returns([
                new PexAplosApiObject { Id = NoTagsAccountNumber.ToString(), Name = "Supplies" },
                new PexAplosApiObject { Id = PurchasesAccountNumber.ToString(), Name = "Travel" },
                new PexAplosApiObject { Id = TaggedAccountNumber.ToString(), Name = "Repairs" }
            ]);

        _mockPexApiClient
            .Setup(client => client.GetBillPayments(It.IsAny<string>(), It.IsAny<BillPaymentListRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BillPaymentListResponseModel
            {
                Items = _bills.Select(bill => bill.Bill).ToList(),
                PageInfo = new PageInfoModel { Page = 1, PageSize = 50, TotalItems = _bills.Count }
            });
        _mockPexApiClient
            .Setup(client => client.GetBillPaymentRequest(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int billId, bool _, CancellationToken _) => _bills.Single(bill => bill.Bill.BillId == billId).Request);
    }

    protected BillPaymentRequestModel AddPexBill(
        PayeeFundsDestinationType fundsDestinationType,
        int billId = PexBillId,
        decimal amount = BillAmount,
        bool linkedCardCharge = true,
        DateTimeOffset? payoutDate = null,
        IEnumerable<TagAnswerModel> tagAnswers = null,
        IEnumerable<string> billNotes = null)
    {
        var request = new BillPaymentRequestModel
        {
            PaymentRequestId = billId,
            PaymentRequestType = PaymentRequestType.BillPay,
            PayeeFundsDestinationType = fundsDestinationType,
            SettlementTransactionId = fundsDestinationType != PayeeFundsDestinationType.BankAccount && linkedCardCharge ? VendorCardTransactionId.ToString() : null,
            MetadataRelationId = MetadataRelationId + billId,
            Amount = amount,
            BillRefNo = $"PEX-{billId}",
            PaymentRequestStatus = PaymentRequestStatus.Closed,
            PaymentRequestStatusTrigger = PaymentRequestStatusTrigger.Paid,
            PayoutDate = payoutDate ?? PayoutDate,
            PaymentId = AchPaymentId,
            Metadata = new PaymentRequestMetadataModel
            {
                TagAnswers = tagAnswers?.ToList(),
                Notes = billNotes?.Select(note => new TransactionNoteModel { NoteText = note }).ToList()
            }
        };

        _bills.Add((new BillPaymentModel
        {
            BillId = billId,
            BillRefNo = request.BillRefNo,
            Amount = amount,
            PayeeName = PayeeName,
            PaymentRequestStatus = PaymentRequestStatus.Closed,
            PaymentRequestStatusTrigger = PaymentRequestStatusTrigger.Paid
        }, request));

        return request;
    }

    protected void UseTags(params string[] tagIds)
    {
        _mockPexApiClient
            .Setup(client => client.IsTagsAvailable(It.IsAny<string>(), It.IsAny<CustomFieldType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockPexApiClient
            .Setup(client => client.GetTags(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tagIds.Select(tagId => new TagDetailsModel { Id = tagId, Type = CustomFieldType.Dropdown }).ToList());
        _mockPexApiClient
            .Setup(client => client.GetDropdownTag(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string tagId, bool _, CancellationToken _) => new TagDropdownDetailsModel { Id = tagId, Options = [] });
    }

    protected Task<Pex2AplosMappingModel> RunPexBillSync(decimal achBankAccountNumber = AchClearingAccountNumber, Action<Pex2AplosMappingModel> configure = null)
        => RunSync(useBillPay: true, achClearingAccountNumber: achBankAccountNumber, configure: mapping =>
        {
            mapping.DefaultAplosFundId = FundId;
            mapping.DefaultAplosTransactionAccountNumber = PurchasesAccountNumber;
            mapping.PexFundsTagId = FundTagId;
            mapping.ExpenseAccountMappings = [new ExpenseAccountMappingModel { ExpenseAccountsPexTagId = PurchasesAccountTagId, DefaultAplosTransactionAccountNumber = PurchasesAccountNumber }];
            configure?.Invoke(mapping);
        });

    protected static string AplosNoteId(int billId) => $"Bill payment #{billId}";
}
