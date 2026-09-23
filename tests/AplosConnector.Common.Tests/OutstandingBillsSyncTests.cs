using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Const;
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

namespace AplosConnector.Common.Tests
{
    public class OutstandingBillsSyncTests
    {
        private const int AplosContactId = 500;
        private const string AplosContactName = "Acme Supplies";
        private static readonly DateTime UtcNow = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IAplosApiClient> _mockAplosApiClient = new();
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory = new();
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService = new();
        private readonly Mock<IPexApiClient> _mockPexApiClient = new();
        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions = new();
        private readonly Mock<SyncHistoryStorage> _mockHistoryStorage = new(MockBehavior.Loose, (TableClient)null);
        private readonly Mock<Pex2AplosMappingStorage> _mockMappingStorage =
            new(MockBehavior.Loose, (TableClient)null, (IStorageMappingService)null, (ILogger)null);

        private readonly List<SyncResultModel> _historyRows = [];
        private readonly List<CreateBillInboxRequestModel> _createdBillInbox = [];
        private readonly List<string> _relationshipNotes = [];
        private readonly List<CreateVendorRequestModel> _createdVendors = [];
        private readonly List<VendorCardCreateOrderRequestModel> _cardOrders = [];

        // The four gate states. Only both-on may write a sync-history row.

        [Fact]
        public async Task Gate_BillPayOffAndToggleOff_WritesNoHistoryRowAndNeverReadsAplos()
        {
            await RunSync(useBillPay: false, syncOutstandingBills: false);

            Assert.Empty(_historyRows);
            _mockAplosApiClient.Verify(c => c.GetPayables(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Gate_BillPayOffAndToggleOn_WritesNoHistoryRowAndNeverReadsAplos()
        {
            var mapping = await RunSync(useBillPay: false, syncOutstandingBills: true);

            Assert.Empty(_historyRows);
            Assert.False(mapping.SyncOutstandingBills);
            Assert.False(mapping.UseBillPayEnabled);
            _mockAplosApiClient.Verify(c => c.GetPayables(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Gate_BillPayOnAndToggleOff_WritesNoHistoryRowAndNeverReadsAplos()
        {
            await RunSync(useBillPay: true, syncOutstandingBills: false);

            Assert.Empty(_historyRows);
            _mockAplosApiClient.Verify(c => c.GetPayables(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Gate_BillPayOnAndToggleOn_ImportsTheBillAndWritesAHistoryRow()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            var request = Assert.Single(_createdBillInbox);
            Assert.Equal(BillInboxSource.Aplos, request.Source);
            Assert.Equal(7, request.VendorId);
            Assert.Equal(AplosContactName, request.VendorName);
            Assert.Equal(125.50m, request.Amount);
            Assert.Equal(new DateTime(2026, 9, 1), request.BillDate);
            Assert.Equal(new DateTime(2026, 10, 1), request.DueDate);
            Assert.Equal("INV-9001", request.BillNumber);

            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncTypes.OutstandingBills, row.SyncType);
            Assert.Equal(SyncStatus.Success.ToString(), row.SyncStatus);
            Assert.Equal(1, row.SyncedRecords);
        }

        // The double filter: only fully unpaid payables import.

        [Fact]
        public async Task FullyPaidPayableIsNeverImported()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 125.50m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Empty(_createdBillInbox);
            Assert.Equal(0, Assert.Single(_historyRows).SyncedRecords);
        }

        [Fact]
        public async Task PartiallyPaidPayableIsNeverImported()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 25.50m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Empty(_createdBillInbox);
            Assert.Equal(0, Assert.Single(_historyRows).SyncedRecords);
        }

        [Theory]
        [InlineData(12550, 0, true)]
        [InlineData(-12550, 0, true)]
        [InlineData(12550, 2550, false)]
        [InlineData(-12550, -2550, false)]
        [InlineData(12550, 12550, false)]
        [InlineData(-12550, -12550, false)]
        [InlineData(0, 0, false)]
        public void SelectUnpaid_KeysOffPaidAndAmountRegardlessOfSign(int amountCents, int paidCents, bool expectImported)
        {
            var selected = AplosPayableFilter.SelectUnpaid([NewPayable("9001", amountCents / 100m, paidCents / 100m)]);

            Assert.Equal(expectImported, selected.Count == 1);
        }

        [Fact]
        public async Task NegativeListAmountIsNormalizedBeforeItReachesPex()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: -125.50m, paid: 0m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal(125.50m, Assert.Single(_createdBillInbox).Amount);
        }

        [Fact]
        public async Task AlreadyImportedPayableIsNotImportedAgain()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));
            SetupExistingBillInboxNote($"{AplosIntegrationService.GetAplosBillSyncedNote("9001")} with ID #42 on 2026-09-10T00:00:00.0000000Z.");

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Empty(_createdBillInbox);
        }

        // Only f_rangestart is sent, so a bill dated past the window end would escape the bill-inbox
        // dedup search and import again on every run.
        [Theory]
        [InlineData(null, "2026-09-11", "2026-09-12")]
        [InlineData("2026-09-05T12:00:00Z", "2026-09-05", "2026-09-06")]
        public async Task APayableDatedPastTheSyncWindowIsNotImported(string endDateUtc, string lastInWindow, string firstPastWindow)
        {
            SetupExistingPexVendor();
            var inWindow = NewPayable("9001", amount: 10m, paid: 0m);
            inWindow.BillDate = DateTime.Parse(lastInWindow);
            var pastWindow = NewPayable("9002", amount: 20m, paid: 0m);
            pastWindow.BillDate = DateTime.Parse(firstPastWindow);
            SetupPayables(inWindow, pastWindow);

            var searches = new List<SearchBillInboxRequestModel>();
            _mockPexApiClient
                .Setup(client => client.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<string, SearchBillInboxRequestModel, int, int, CancellationToken>((_, request, _, _, _) => searches.Add(request))
                .ReturnsAsync(new SearchBillInboxResponseModel { Items = [], PageInfo = new PageInfoModel() });

            await RunSync(useBillPay: true, syncOutstandingBills: true,
                endDateUtc: endDateUtc == null ? null : DateTime.Parse(endDateUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal));

            var request = Assert.Single(_createdBillInbox);
            Assert.Equal(inWindow.BillDate, request.BillDate);
            var search = Assert.Single(searches);
            Assert.True(search.BillDateFrom <= new DateTime(2026, 8, 1));
            Assert.True(search.BillDateTo >= inWindow.BillDate);
        }

        [Fact]
        public async Task APayableDatedBeforeTheSyncWindowIsNotImported()
        {
            SetupExistingPexVendor();
            var inWindow = NewPayable("9001", amount: 10m, paid: 0m);
            var beforeWindow = NewPayable("9002", amount: 20m, paid: 0m);
            beforeWindow.BillDate = new DateTime(2026, 6, 1);
            SetupPayables(inWindow, beforeWindow);

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal("INV-9001", Assert.Single(_createdBillInbox).BillNumber);
        }

        [Fact]
        public async Task AVendorSetupFailureIsReportedAsSuchNotAsAMissingAddress()
        {
            SetupContact(NewContact(AplosContactId, AplosContactName, street1: "1 Main St", city: "Austin", state: "TX", postalCode: "73301", email: "ap@acme.example"));
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));
            _mockPexApiClient
                .Setup(client => client.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("PEX unavailable"));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Empty(_createdBillInbox);
            var row = Assert.Single(_historyRows);
            Assert.Contains("Could not create or approve the PEX vendor for this contact.", row.SyncNotes);
            Assert.DoesNotContain("billing address", row.SyncNotes);
        }

        [Fact]
        public async Task PayablesAreReadFromTheEstCalendarDayOfTheWindowStart()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 10m, paid: 0m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            // EarliestTransactionDateToSync is 2026-08-01T00:00Z, which is still 2026-07-31 in EST.
            _mockAplosApiClient.Verify(c => c.GetPayables(new DateOnly(2026, 7, 31), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AnImportNoteOnALaterSearchPageStillPreventsAReimport()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));
            var firstPage = Enumerable.Range(1, 100).Select(i => new BillInboxModel { BillInboxId = 1000 + i }).ToList();
            _mockPexApiClient
                .Setup(client => client.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchBillInboxResponseModel { Items = firstPage, PageInfo = new PageInfoModel { Page = 1, PageSize = 100, TotalItems = 101 } });
            _mockPexApiClient
                .Setup(client => client.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), 2, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchBillInboxResponseModel
                {
                    Items = [new BillInboxModel
                    {
                        BillInboxId = 42,
                        Metadata = new PaymentRequestMetadataModel { Notes = [new TransactionNoteModel { NoteText = $"{AplosIntegrationService.GetAplosBillSyncedNote("9001")} with ID #42 on 2026-09-10T00:00:00.0000000Z." }] }
                    }],
                    PageInfo = new PageInfoModel { Page = 2, PageSize = 100, TotalItems = 101 }
                });

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Empty(_createdBillInbox);
        }

        // Known gap until 148047: the note is the only dedup key, so a bill created without one imports again.
        [Fact]
        public async Task ANoteWriteFailureAfterTheBillIsCreatedLeavesItToImportAgain()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));
            _mockPexApiClient
                .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("PEX unavailable"));

            await RunSync(useBillPay: true, syncOutstandingBills: true);
            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal(2, _createdBillInbox.Count);
            Assert.Equal(2, _historyRows.Count);
            Assert.All(_historyRows, row => Assert.Contains("INV-9001", row.SyncNotes));
        }

        [Fact]
        public async Task AnAuditNoteIsWrittenAgainstTheCreatedBillInboxItem()
        {
            SetupExistingPexVendor();
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Contains(AplosIntegrationService.GetAplosBillSyncedNote("9001"), Assert.Single(_relationshipNotes));
        }

        // Per-bill contact validation: the bad bill fails by name, the good bill still imports.

        [Fact]
        public async Task ContactWithoutAnEmailFailsOnlyThatBill()
        {
            SetupExistingPexVendor();
            SetupContact(NewContact(AplosContactId + 1, "Beta Services", street1: "2 Elm St", city: "Austin", state: "TX", postalCode: "73301", email: null));
            SetupPayables(
                NewPayable("9001", amount: 125.50m, paid: 0m),
                NewPayable("9002", amount: 40.00m, paid: 0m, contactId: AplosContactId + 1, contactName: "Beta Services"));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal("INV-9001", Assert.Single(_createdBillInbox).BillNumber);

            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Partial.ToString(), row.SyncStatus);
            Assert.Equal(1, row.SyncedRecords);
            Assert.Contains("INV-9002", row.SyncNotes);
            Assert.Contains("Add an Email for the Beta Services contact in Aplos.", row.SyncNotes);
        }

        [Fact]
        public async Task ContactWithAnIncompleteAddressFailsOnlyThatBillAndNamesTheMissingFields()
        {
            SetupExistingPexVendor();
            SetupContact(NewContact(AplosContactId + 1, "Beta Services", street1: "2 Elm St", city: null, state: "TX", postalCode: null, email: "ap@beta.example"));
            SetupPayables(
                NewPayable("9001", amount: 125.50m, paid: 0m),
                NewPayable("9002", amount: 40.00m, paid: 0m, contactId: AplosContactId + 1, contactName: "Beta Services"));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal("INV-9001", Assert.Single(_createdBillInbox).BillNumber);

            var row = Assert.Single(_historyRows);
            Assert.Equal(SyncStatus.Partial.ToString(), row.SyncStatus);
            Assert.Contains("INV-9002", row.SyncNotes);
            Assert.Contains("Missing: City, Postal Code.", row.SyncNotes);
        }

        [Fact]
        public async Task AMissingPexVendorIsCreatedApprovedAndGivenACardBeforeTheBillIsImported()
        {
            SetupContact(NewContact(AplosContactId, AplosContactName, street1: "1 Main St", city: "Austin", state: "TX", postalCode: "73301", email: "ap@acme.example"));
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            var createdVendor = Assert.Single(_createdVendors);
            Assert.Equal(AplosContactName, createdVendor.VendorName);
            Assert.Equal("ap@acme.example", createdVendor.EmailForRemittance);
            Assert.Equal($"APLOS{AplosContactId}", createdVendor.CustomId);
            Assert.Equal("1 Main St", createdVendor.VendorAddress.AddressLine1);

            var cardOrder = Assert.Single(_cardOrders);
            Assert.Equal(AplosContactName, Assert.Single(cardOrder.VendorCards).VendorName);

            Assert.Single(_createdBillInbox);
        }

        [Fact]
        public async Task ALongVendorNameIsCutToTheCardLimitAndItsCardIsStillLinked()
        {
            const string longName = "Acme Office Supplies Inc";
            SetupContact(NewContact(AplosContactId, longName, street1: "1 Main St", city: "Austin", state: "TX", postalCode: "73301", email: "ap@acme.example"));
            SetupPayables(NewPayable("9001", amount: 125.50m, paid: 0m, contactName: longName));
            _mockPexApiClient
                .Setup(client => client.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorModel { VendorId = 11, VendorName = longName, CustomId = $"APLOS{AplosContactId}" });
            _mockPexApiClient
                .Setup(client => client.GetVendorCardOrder(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorCardOrderResponseModel { CardOrderId = 99, Cards = [new VendorCardOrderItemResponse { AcctId = 321, VendorName = "Acme Office Sup" }] });

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal("Acme Office Sup", Assert.Single(Assert.Single(_cardOrders).VendorCards).VendorName);
            _mockPexApiClient.Verify(client => client.AddVendorCard(It.IsAny<string>(), 11, It.Is<AddVendorCardRequestModel>(r => r.CardholderAcctId == 321), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NewVendorsSharingACutCardNameEachGetAnIdSuffixedCard()
        {
            SetupNewVendors((AplosContactId, "Acme Office Supplies Inc"), (AplosContactId + 1, "Acme Office Supply Co"));
            SetupCardOrderResponse(("Acme Office 11", 321), ("Acme Office 12", 322));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal(new[] { "Acme Office 11", "Acme Office 12" }, Assert.Single(_cardOrders).VendorCards.Select(c => c.VendorName).OrderBy(n => n));
            VerifyCardLinked(vendorId: 11, cardAcctId: 321);
            VerifyCardLinked(vendorId: 12, cardAcctId: 322);
        }

        [Fact]
        public async Task ANewVendorWhoseCutNameMatchesAnExistingCardholderGetsAnIdSuffixedCard()
        {
            SetupNewVendors((AplosContactId, "Acme Office Supplies Inc"));
            _mockPexApiClient
                .Setup(client => client.GetBusinessDetails(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessDetailsModel
                {
                    CHAccountList = [new CardholderAccountModel { AccountId = 900, LastName = "Acme Office Sup", CardholderType = CardholderType.Vendor.ToString(), AccountStatus = "OPEN" }]
                });
            SetupCardOrderResponse(("Acme Office 11", 321));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            Assert.Equal("Acme Office 11", Assert.Single(Assert.Single(_cardOrders).VendorCards).VendorName);
            VerifyCardLinked(vendorId: 11, cardAcctId: 321);
            _mockPexApiClient.Verify(client => client.AddVendorCard(It.IsAny<string>(), It.IsAny<int>(), It.Is<AddVendorCardRequestModel>(r => r.CardholderAcctId == 900), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnAmbiguousCardOrderResponseLinksNoCard()
        {
            SetupNewVendors((AplosContactId, "Acme Office Supplies Inc"));
            SetupCardOrderResponse(("Acme Office Sup", 321), ("Acme Office Sup", 322));

            await RunSync(useBillPay: true, syncOutstandingBills: true);

            _mockPexApiClient.Verify(client => client.AddVendorCard(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AddVendorCardRequestModel>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void SyncOutstandingBillsSurvivesTheEntityAndSettingsRoundTrips()
        {
            var service = new StorageMappingService(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
            var original = new Pex2AplosMappingModel { PEXBusinessAcctId = 12345, SyncOutstandingBills = true };

            Assert.True(service.Map(service.Map(original)).SyncOutstandingBills);

            var rebuilt = new Pex2AplosMappingModel { PEXBusinessAcctId = 12345 };
            rebuilt.UpdateFromSettings(original.ToStorageModel());
            Assert.True(rebuilt.SyncOutstandingBills);
        }

        public OutstandingBillsSyncTests()
        {
            SetupDefaults();
        }

        private async Task<Pex2AplosMappingModel> RunSync(bool useBillPay, bool syncOutstandingBills, DateTime? endDateUtc = null)
        {
            var mapping = NewMapping(syncOutstandingBills);
            mapping.EndDateUtc = endDateUtc;

            _mockPexApiClient
                .Setup(client => client.GetBusinessSettings(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessSettingsModel { UseBillPay = useBillPay });

            await GetAplosIntegrationService().SyncOutstandingBills(NullLogger.Instance, mapping, UtcNow, default);
            return mapping;
        }

        private static Pex2AplosMappingModel NewMapping(bool syncOutstandingBills) => new()
        {
            PEXBusinessAcctId = 6118231,
            PEXExternalAPIToken = "token",
            AplosAuthenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            AplosAccountId = "accountId",
            AplosClientId = "clientId",
            AplosPrivateKey = "privateKey",
            IsManualSync = true,
            EarliestTransactionDateToSync = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            SyncOutstandingBills = syncOutstandingBills
        };

        private static AplosApiPayableDetail NewPayable(
            string id, decimal amount, decimal paid, int contactId = AplosContactId, string contactName = AplosContactName) => new()
            {
                Id = id,
                BillDate = new DateTime(2026, 9, 1),
                DueDate = new DateTime(2026, 10, 1),
                ReferenceNumber = $"INV-{id}",
                Amount = amount,
                PaidAmount = paid,
                Contact = new AplosApiContactDetail { Id = contactId, CompanyName = contactName, Type = "company" }
            };

        private static AplosApiContactDetail NewContact(
            int id, string companyName, string street1, string city, string state, string postalCode, string email) => new()
            {
                Id = id,
                CompanyName = companyName,
                Type = "company",
                Email = email,
                Addresses = [new AplosApiContactAddressDetail
                {
                    IsPrimary = true,
                    Street1 = street1,
                    City = city,
                    State = state,
                    PostalCode = postalCode
                }]
            };

        private void SetupPayables(params AplosApiPayableDetail[] payables)
        {
            _mockAplosApiClient
                .Setup(client => client.GetPayables(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(payables.ToList());
        }

        private void SetupNewVendors(params (int ContactId, string Name)[] contacts)
        {
            var namesById = new Dictionary<int, string>();
            var nextVendorId = 11;
            foreach (var (contactId, name) in contacts)
            {
                SetupContact(NewContact(contactId, name, street1: "1 Main St", city: "Austin", state: "TX", postalCode: "73301", email: "ap@acme.example"));
            }
            SetupPayables(contacts.Select((c, i) => NewPayable($"{9001 + i}", amount: 10m, paid: 0m, contactId: c.ContactId, contactName: c.Name)).ToArray());

            _mockPexApiClient
                .Setup(client => client.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()))
                .Callback<string, CreateVendorRequestModel, CancellationToken>((_, request, _) => _createdVendors.Add(request))
                .ReturnsAsync((string _, CreateVendorRequestModel request, CancellationToken _) =>
                {
                    var id = nextVendorId++;
                    namesById[id] = request.VendorName;
                    return new VendorModel { VendorId = id, VendorName = request.VendorName, CustomId = request.CustomId };
                });
            _mockPexApiClient
                .Setup(client => client.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, int id, CancellationToken _) => new VendorModel { VendorId = id, VendorName = namesById[id] });
        }

        private void SetupCardOrderResponse(params (string VendorName, int AcctId)[] cards)
        {
            _mockPexApiClient
                .Setup(client => client.GetVendorCardOrder(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorCardOrderResponseModel
                {
                    CardOrderId = 99,
                    Cards = cards.Select(c => new VendorCardOrderItemResponse { AcctId = c.AcctId, VendorName = c.VendorName }).ToList()
                });
        }

        private void VerifyCardLinked(int vendorId, int cardAcctId) =>
            _mockPexApiClient.Verify(client => client.AddVendorCard(It.IsAny<string>(), vendorId, It.Is<AddVendorCardRequestModel>(r => r.CardholderAcctId == cardAcctId), It.IsAny<CancellationToken>()), Times.Once);

        private void SetupContact(AplosApiContactDetail contact)
        {
            _mockAplosApiClient
                .Setup(client => client.GetContact(contact.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AplosApiContactResponse { Data = new AplosApiContactData { Contact = contact } });
        }

        private void SetupExistingPexVendor()
        {
            _mockPexApiClient
                .Setup(client => client.GetVendors(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorListResponseModel
                {
                    Vendors = [new VendorModel { VendorId = 7, VendorName = AplosContactName, CustomId = $"APLOS{AplosContactId}" }]
                });
        }

        private void SetupExistingBillInboxNote(string noteText)
        {
            _mockPexApiClient
                .Setup(client => client.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchBillInboxResponseModel
                {
                    Items = [new BillInboxModel
                    {
                        BillInboxId = 42,
                        Metadata = new PaymentRequestMetadataModel { Notes = [new TransactionNoteModel { NoteText = noteText }] }
                    }],
                    PageInfo = new PageInfoModel { Page = 1, PageSize = 100, TotalItems = 1 }
                });
        }

        private void SetupDefaults()
        {
            _mockOptions.Setup(options => options.Value).Returns(new AppSettingsModel());

            _mockHistoryStorage
                .Setup(storage => storage.CreateAsync(It.IsAny<SyncResultModel>(), It.IsAny<CancellationToken>()))
                .Callback<SyncResultModel, CancellationToken>((result, _) => _historyRows.Add(result))
                .Returns(Task.CompletedTask);

            _mockMappingStorage
                .Setup(storage => storage.UpdateAsync(It.IsAny<Pex2AplosMappingModel>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mockPexApiClient
                .Setup(client => client.GetVendors(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorListResponseModel { Vendors = [] });

            _mockPexApiClient
                .Setup(client => client.GetBusinessDetails(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessDetailsModel { CHAccountList = [] });

            _mockPexApiClient
                .Setup(client => client.SearchBillInbox(It.IsAny<string>(), It.IsAny<SearchBillInboxRequestModel>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchBillInboxResponseModel { Items = [], PageInfo = new PageInfoModel() });

            _mockPexApiClient
                .Setup(client => client.CreateVendor(It.IsAny<string>(), It.IsAny<CreateVendorRequestModel>(), It.IsAny<CancellationToken>()))
                .Callback<string, CreateVendorRequestModel, CancellationToken>((_, request, _) => _createdVendors.Add(request))
                .ReturnsAsync((string _, CreateVendorRequestModel request, CancellationToken _) =>
                    new VendorModel { VendorId = 11, VendorName = request.VendorName, CustomId = request.CustomId });

            _mockPexApiClient
                .Setup(client => client.ApproveVendor(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorModel { VendorId = 11, VendorName = AplosContactName, CustomId = $"APLOS{AplosContactId}" });

            _mockPexApiClient
                .Setup(client => client.GetMyAdminProfile(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessAdminReponseModel { Admin = new BusinessAdminModel { Email = "admin@example.com", Phone = "5125550100" } });

            _mockPexApiClient
                .Setup(client => client.CreateVendorCardOrder(It.IsAny<string>(), It.IsAny<VendorCardCreateOrderRequestModel>(), It.IsAny<CancellationToken>()))
                .Callback<string, VendorCardCreateOrderRequestModel, CancellationToken>((_, request, _) => _cardOrders.Add(request))
                .ReturnsAsync(new VendorCardCreateOrderResponseModel { VendorCardOrderId = 99, NumberOfCardsRequested = 1 });

            _mockPexApiClient
                .Setup(client => client.GetVendorCardOrder(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorCardOrderResponseModel
                {
                    CardOrderId = 99,
                    Cards = [new VendorCardOrderItemResponse { AcctId = 321, VendorName = AplosContactName }]
                });

            _mockPexApiClient
                .Setup(client => client.SetDefaultVendorCard(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VendorModel { VendorId = 11, VendorName = AplosContactName, CustomId = $"APLOS{AplosContactId}" });

            var nextBillInboxId = 100;
            _mockPexApiClient
                .Setup(client => client.CreateBillInbox(It.IsAny<string>(), It.IsAny<CreateBillInboxRequestModel>(), It.IsAny<CancellationToken>()))
                .Callback<string, CreateBillInboxRequestModel, CancellationToken>((_, request, _) => _createdBillInbox.Add(request))
                .ReturnsAsync(() => new BillInboxModel { BillInboxId = nextBillInboxId, MetadataId = nextBillInboxId++ });

            _mockPexApiClient
                .Setup(client => client.AddTransactionRelationshipNote(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, long, string, CancellationToken>((_, _, noteText, _) => _relationshipNotes.Add(noteText))
                .Returns(Task.CompletedTask);

            _mockAplosApiClientFactory
                .Setup(factory => factory.CreateClient(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Uri>(),
                    It.IsAny<Func<ILogger, AplosAuthModel>>(),
                    It.IsAny<Func<AplosAuthModel, ILogger, CancellationToken, Task>>()))
                .Returns(_mockAplosApiClient.Object);
        }

        private AplosIntegrationService GetAplosIntegrationService()
        {
            return new AplosIntegrationService(
                new NullLogger<AplosIntegrationService>(),
                _mockOptions.Object,
                _mockAplosApiClientFactory.Object,
                _mockAplosIntegrationMappingService.Object,
                _mockPexApiClient.Object,
                _mockHistoryStorage.Object,
                _mockMappingStorage.Object,
                new SyncSettingsModel(),
                null);
        }
    }
}
