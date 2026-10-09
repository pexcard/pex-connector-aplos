using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using Azure.Data.Tables;
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

namespace AplosConnector.Common.Tests;

// The vendor half of the outstanding-bills import. Repair runs only for contacts with a new bill, so a second
// RunSync (no bill mapping is kept here) stands in for that contact's next bill.
public abstract class VendorSetupTestBase
{
    protected const int BusinessId = 6118231;
    protected const int ContactId = 500;
    protected const string ContactName = "Acme Supplies";
    protected const int VendorId = 7;
    protected const int CardOrderId = 99;
    protected const int CardAcctId = 321;
    protected static readonly DateTime UtcNow = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAplosApiClient> _aplosApiClient = new();
    private readonly Mock<IAplosApiClientFactory> _aplosApiClientFactory = new();
    private readonly Mock<SyncHistoryStorage> _historyStorage = new(MockBehavior.Loose, (TableClient)null);
    private readonly Mock<IAplosBillMappingStorage> _billMappingStorage = new();

    protected readonly Mock<IPexApiClient> PexApiClient = new();
    protected readonly FakeVendorCardOrderStorage CardOrderStorage = new();
    protected readonly List<SyncResultModel> HistoryRows = [];
    protected readonly List<CreateBillInboxRequestModel> CreatedBillInbox = [];
    protected readonly List<VendorCardCreateOrderRequestModel> CardOrders = [];

    protected VendorSetupTestBase()
    {
        _historyStorage
            .Setup(s => s.CreateAsync(It.IsAny<SyncResultModel>(), It.IsAny<CancellationToken>()))
            .Callback<SyncResultModel, CancellationToken>((row, _) => HistoryRows.Add(row))
            .Returns(Task.CompletedTask);
        _billMappingStorage
            .Setup(s => s.GetByBusinessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _aplosApiClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Uri>(),
                It.IsAny<Func<ILogger, AplosAuthModel>>(), It.IsAny<Func<AplosAuthModel, ILogger, CancellationToken, Task>>()))
            .Returns(_aplosApiClient.Object);
        SetupBills((ContactId, ContactName));
        _aplosApiClient
            .Setup(c => c.GetContact(ContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AplosApiContactResponse
            {
                Data = new AplosApiContactData
                {
                    Contact = new AplosApiContactDetail
                    {
                        Id = ContactId,
                        CompanyName = ContactName,
                        Type = "company",
                        Email = "ap@acme.example",
                        Addresses = [new AplosApiContactAddressDetail { IsPrimary = true, Street1 = "1 Main St", City = "Austin", State = "TX", PostalCode = "73301" }]
                    }
                }
            });

        SetupPexVendors();
        PexApiClient
            .Setup(c => c.GetBusinessDetails(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessDetailsModel { CHAccountList = [] });
        PexApiClient
            .Setup(c => c.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchBillInboxResponseModel { Items = [], PageInfo = new PageInfoModel() });
        PexApiClient
            .Setup(c => c.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CreateVendorRequestModel request, CancellationToken _) =>
                new VendorModel { VendorId = VendorId, VendorName = request.VendorName, CustomId = request.CustomId, VendorStatus = VendorStatus.Pending });
        SetupApproval(VendorStatus.Onboarded);
        PexApiClient
            .Setup(c => c.GetMyAdminProfile(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessAdminReponseModel { Admin = new BusinessAdminModel { Email = "admin@example.com", Phone = "5125550100" } });
        PexApiClient
            .Setup(c => c.CreateVendorCardOrder(It.IsAny<string>(), It.IsAny<VendorCardCreateOrderRequestModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, VendorCardCreateOrderRequestModel, CancellationToken>((_, request, _) => CardOrders.Add(request))
            .ReturnsAsync(new VendorCardCreateOrderResponseModel { VendorCardOrderId = CardOrderId, NumberOfCardsRequested = 1 });
        SetupProvisionedCard(CardAcctId);
        PexApiClient
            .Setup(c => c.CreateBillInbox(It.IsAny<string>(), It.IsAny<CreateBillInboxRequestModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, CreateBillInboxRequestModel, CancellationToken>((_, request, _) => CreatedBillInbox.Add(request))
            .ReturnsAsync(new BillInboxModel { BillInboxId = 100, MetadataId = 100 });
    }

    protected async Task RunSync()
    {
        var mapping = new Pex2AplosMappingModel
        {
            PEXBusinessAcctId = BusinessId,
            PEXExternalAPIToken = "token",
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            EarliestTransactionDateToSync = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            SyncOutstandingBills = true,
            UseBillPayEnabled = true,
            BillPaymentsAchClearingAccountNumber = 1010m,
            BillPaymentsCardClearingAccountNumber = 1020m
        };

        CreatedBillInbox.Clear();
        HistoryRows.Clear();
        await new AplosIntegrationService(
            new NullLogger<AplosIntegrationService>(),
            Mock.Of<IOptions<AppSettingsModel>>(o => o.Value == new AppSettingsModel()),
            _aplosApiClientFactory.Object,
            Mock.Of<IAplosIntegrationMappingService>(),
            PexApiClient.Object,
            _historyStorage.Object,
            null,
            new SyncSettingsModel(),
            null,
            _billMappingStorage.Object,
            CardOrderStorage).SyncOutstandingBills(NullLogger.Instance, mapping, UtcNow, default);
    }

    // One bill per contact, numbered INV-9001 onwards.
    protected void SetupBills(params (int ContactId, string Name)[] contacts) =>
        _aplosApiClient
            .Setup(c => c.GetPayables(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contacts.Select((contact, i) => new AplosApiPayableDetail
            {
                Id = $"{9001 + i}",
                BillDate = new DateTime(2026, 9, 1),
                DueDate = new DateTime(2026, 10, 1),
                ReferenceNumber = $"INV-{9001 + i}",
                Amount = 10m,
                Contact = new AplosApiContactDetail { Id = contact.ContactId, CompanyName = contact.Name, Type = "company" }
            }).ToList());

    protected void SetupPexVendors(params VendorModel[] vendors) =>
        PexApiClient
            .Setup(c => c.GetVendors(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VendorListResponseModel { Vendors = [.. vendors] });

    protected static VendorModel ConnectorVendor(VendorStatus status = VendorStatus.Onboarded, int vendorId = VendorId, int contactId = ContactId, string name = ContactName)
        => new() { VendorId = vendorId, VendorName = name, CustomId = $"APLOS{contactId}", VendorStatus = status };

    protected void SetupApproval(VendorStatus resultingStatus) =>
        PexApiClient
            .Setup(c => c.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int id, CancellationToken _) => new VendorModel { VendorId = id, VendorName = ContactName, VendorStatus = resultingStatus });

    protected void SetupProvisionedCard(int? acctId, int orderId = CardOrderId, params (string Name, int AcctId)[] moreCards) =>
        PexApiClient
            .Setup(c => c.GetVendorCardOrder(It.IsAny<string>(), orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VendorCardOrderResponseModel
            {
                CardOrderId = orderId,
                Cards = [new VendorCardOrderItemResponse { AcctId = acctId, VendorName = ContactName }, .. moreCards.Select(c => new VendorCardOrderItemResponse { AcctId = c.AcctId, VendorName = c.Name })]
            });

    protected void SeedCardOrder(int? cardAcctId, int? orderId = CardOrderId, int contactId = ContactId, int vendorId = VendorId, string cardName = ContactName) =>
        CardOrderStorage.Seed(new AplosVendorCardOrderModel
        {
            PEXBusinessAcctId = BusinessId,
            AplosContactId = contactId,
            PexVendorId = vendorId,
            CardName = cardName,
            CardOrderId = orderId,
            CardAcctId = cardAcctId,
            LinkedUtc = cardAcctId.HasValue ? UtcNow : null
        });

    protected void VerifyCardLinked(Times times, int vendorId = VendorId, int cardAcctId = CardAcctId) =>
        PexApiClient.Verify(c => c.AddVendorCard(It.IsAny<string>(), vendorId, It.Is<AddVendorCardRequestModel>(r => r.CardholderAcctId == cardAcctId), It.IsAny<CancellationToken>()), times);

    protected void AssertSucceeded() => Assert.Equal(SyncStatus.Success.ToString(), Assert.Single(HistoryRows).SyncStatus);

    protected void AssertReported(string text, string billNumber = "INV-9001")
    {
        var row = Assert.Single(HistoryRows);
        Assert.Equal(SyncStatus.Partial.ToString(), row.SyncStatus);
        Assert.Contains($"Bill {billNumber}: ", row.SyncNotes);
        Assert.Contains(text, row.SyncNotes);
    }
}
