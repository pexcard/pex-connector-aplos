using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models.Single;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PexCard.Api.Client.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class AplosIntegrationServiceTests
    {
        private readonly Mock<IAplosApiClient> _mockAplosApiClient;

        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions;
        private readonly SyncSettingsModel _mockSettings;
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory;
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService;
        private readonly Mock<IPexApiClient> _mockPexApiClient;

        public AplosIntegrationServiceTests()
        {
            _mockAplosApiClient = new Mock<IAplosApiClient>();

            _mockOptions = new Mock<IOptions<AppSettingsModel>>();
            _mockSettings = new SyncSettingsModel();
            _mockAplosApiClientFactory = new Mock<IAplosApiClientFactory>();
            _mockAplosIntegrationMappingService = new Mock<IAplosIntegrationMappingService>();
            _mockPexApiClient = new Mock<IPexApiClient>();
        }

        [Fact]
        public async Task GetAplosAccount_ReturnsAplosApiObject_WhenAplosAccountFound()
        {
            //Arrange
            var aplosAccount = new AplosApiAccountDetail { AccountNumber = 123, Name = "test" };
            var aplosAccountResponse = new AplosApiAccountResponse { Data = new AplosApiAccountData { Account = aplosAccount, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccount(aplosAccount.AccountNumber, default))
                .Returns(Task.FromResult(aplosAccountResponse));

            var mappedAplosAccount = new PexAplosApiObject { Id = aplosAccount.AccountNumber.ToString(), Name = aplosAccount.Name, };
            _mockAplosIntegrationMappingService.Setup(mockMappingService => mockMappingService.Map(aplosAccount)).Returns(mappedAplosAccount);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosAccount = await service.GetAplosAccount(GetMapping(), aplosAccount.AccountNumber, default);

            //Assert
            Assert.Equal(mappedAplosAccount, actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosAccount_ReturnsNull_WhenAplosAccountNotFound()
        {
            //Arrange
            var aplosAccountResponse = new AplosApiAccountResponse { Data = new AplosApiAccountData { Account = default, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccount(It.IsAny<decimal>(), default))
                .Returns(Task.FromResult(aplosAccountResponse));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiAccountDetail>()))
                .Returns(default(PexAplosApiObject));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosAccount = await service.GetAplosAccount(GetMapping(), It.IsAny<decimal>(), default);

            //Assert
            Assert.Null(actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosAccounts_ReturnsAplosApiObject_WhenAplosAccountsFound()
        {
            //Arrange
            var aplosAccount = new AplosApiAccountDetail { AccountNumber = 123, Name = "test" };
            var aplosAccounts = new List<AplosApiAccountDetail> { aplosAccount };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccounts(It.IsAny<string>(), default))
                .Returns(Task.FromResult(aplosAccounts));

            IEnumerable<PexAplosApiObject> mappedAplosAccount = new[] { new PexAplosApiObject { Id = aplosAccount.AccountNumber.ToString(), Name = aplosAccount.Name, } };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosAccounts))
                .Returns(mappedAplosAccount);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            var actualMappedAplosAccount = await service.GetAplosAccounts(GetMapping());

            //Assert
            Assert.Equal(mappedAplosAccount, actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosAccounts_ReturnsEmptyCollection_WhenAplosAccountsNotFound()
        {
            //Arrange
            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccounts(It.IsAny<string>(), default))
                .Returns(Task.FromResult(default(List<AplosApiAccountDetail>)));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiAccountDetail[]>()))
                .Returns(default(PexAplosApiObject[]));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosAccount = await service.GetAplosAccounts(GetMapping());

            //Assert
            Assert.NotNull(actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosContact_ReturnsAplosApiObject_WhenAplosContactFound()
        {
            //Arrange
            var aplosContact = new AplosApiContactDetail { Id = 123, CompanyName = "test" };
            var aplosContactResponse = new AplosApiContactResponse { Data = new AplosApiContactData { Contact = aplosContact, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContact(aplosContact.Id, default))
                .Returns(Task.FromResult(aplosContactResponse));

            var mappedAplosContact = new PexAplosApiObject { Id = aplosContact.Id.ToString(), Name = aplosContact.CompanyName, };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosContact))
                .Returns(mappedAplosContact);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosContact = await service.GetAplosContact(GetMapping(), aplosContact.Id, default);

            //Assert
            Assert.Equal(mappedAplosContact, actualMappedAplosContact);
        }

        [Fact]
        public async Task GetAplosContact_ReturnsNull_WhenAplosContactNotFound()
        {
            //Arrange
            var aplosContactResponse = new AplosApiContactResponse { Data = new AplosApiContactData { Contact = default, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContact(It.IsAny<int>(), default))
                .Returns(Task.FromResult(aplosContactResponse));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiContactDetail>()))
                .Returns(default(PexAplosApiObject));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosContact = await service.GetAplosContact(GetMapping(), It.IsAny<int>(), default);

            //Assert
            Assert.Null(actualMappedAplosContact);
        }

        [Fact]
        public async Task GetAplosContacts_ReturnsAplosApiObject_WhenAplosContactsFound()
        {
            //Arrange
            var aplosContact = new AplosApiContactDetail { Id = 123, CompanyName = "test" };
            var aplosContacts = new List<AplosApiContactDetail> { aplosContact };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContacts(default))
                .Returns(Task.FromResult(aplosContacts));

            IEnumerable<PexAplosApiObject> mappedAplosContacts = new[] { new PexAplosApiObject { Id = aplosContact.Id.ToString(), Name = aplosContact.CompanyName, } };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosContacts))
                .Returns(mappedAplosContacts);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosContats = await service.GetAplosContacts(GetMapping(), default);

            //Assert
            Assert.Equal(mappedAplosContacts, actualMappedAplosContats);
        }

        [Fact]
        public async Task GetAplosContacts_ReturnsNull_WhenAplosContactsNotFound()
        {
            //Arrange
            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContacts(default))
                .Returns(Task.FromResult(default(List<AplosApiContactDetail>)));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiContactDetail[]>()))
                .Returns(default(PexAplosApiObject[]));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosContactList = await service.GetAplosContacts(GetMapping(), default);

            //Assert
            Assert.Null(mappedAplosContactList);
        }

        [Fact]
        public async Task GetAplosFund_ReturnsAplosApiObject_WhenAplosFundFound()
        {
            //Arrange
            var aplosFund = new AplosApiFundDetail { Id = 123, Name = "test" };
            var aplosFundResponse = new AplosApiFundResponse { Data = new AplosApiFundData { Fund = aplosFund, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFund(aplosFund.Id, default))
                .Returns(Task.FromResult(aplosFundResponse));

            var mappedAplosFund = new PexAplosApiObject { Id = aplosFund.Id.ToString(), Name = aplosFund.Name, };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosFund))
                .Returns(mappedAplosFund);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosFund = await service.GetAplosFund(GetMapping(), aplosFund.Id, default);

            //Assert
            Assert.Equal(mappedAplosFund, actualMappedAplosFund);
        }

        [Fact]
        public async Task GetAplosFund_ReturnsNull_WhenAplosFundNotFound()
        {
            //Arrange
            var aplosFundResponse = new AplosApiFundResponse { Data = new AplosApiFundData { Fund = default, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFund(It.IsAny<int>(), default))
                .Returns(Task.FromResult(aplosFundResponse));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiFundDetail>()))
                .Returns(default(PexAplosApiObject));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosFund = await service.GetAplosFund(GetMapping(), It.IsAny<int>(), default);

            //Assert
            Assert.Null(actualMappedAplosFund);
        }

        [Fact]
        public async Task GetAplosFunds_ReturnsAplosApiObject_WhenAplosFundsFound()
        {
            //Arrange
            var aplosFund = new AplosApiFundDetail { Id = 123, Name = "test" };
            var aplosFunds = new List<AplosApiFundDetail> { aplosFund };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFunds(default))
                .Returns(Task.FromResult(aplosFunds));

            IEnumerable<PexAplosApiObject> mappedAplosFunds = new[] { new PexAplosApiObject { Id = aplosFund.Id.ToString(), Name = aplosFund.Name, } };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosFunds))
                .Returns(mappedAplosFunds);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosFunds = await service.GetAplosFunds(GetMapping(), default);

            //Assert
            Assert.Equal(mappedAplosFunds, actualMappedAplosFunds);
        }

        [Fact]
        public async Task GetAplosFunds_ReturnsNull_WhenAplosFundsNotFound()
        {
            //Arrange
            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFunds(default))
                .Returns(Task.FromResult(default(List<AplosApiFundDetail>)));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiFundDetail[]>()))
                .Returns(default(PexAplosApiObject[]));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosFunds = await service.GetAplosFunds(GetMapping(), default);

            //Assert
            Assert.Null(actualMappedAplosFunds);
        }

        [Fact]
        public void GetFlattenedAplosTagValues_ReturnsFlattenedList_FromValidTagCategory()
        {
            //Arrange
            var expectedAplosTagValues = new List<AplosApiTagDetail>
            {
                new AplosApiTagDetail
                {
                    Id = "1",
                    Name = "11",
                },
                new AplosApiTagDetail
                {
                    Id = "2",
                    Name = "22",
                },
                new AplosApiTagDetail
                {
                    Id = "3",
                    Name = "33",
                },
                new AplosApiTagDetail
                {
                    Id = "4",
                    Name = "44",
                },
                new AplosApiTagDetail
                {
                    Id = "5",
                    Name = "55",
                },
                new AplosApiTagDetail
                {
                    Id = "6",
                    Name = "66",
                },
            };

            var aplosTagCategory = new AplosApiTagCategoryDetail
            {
                TagGroups = new List<AplosApiTagGroupDetail>
                {
                    new AplosApiTagGroupDetail
                    {
                        Tags = new List<AplosApiTagDetail>
                        {
                            new AplosApiTagDetail
                            {
                                Id = expectedAplosTagValues[0].Id,
                                Name = expectedAplosTagValues[0].Name,
                                SubTags = new List<AplosApiTagDetail>
                                {
                                    expectedAplosTagValues[1],
                                    expectedAplosTagValues[2]
                                },
                            },
                            expectedAplosTagValues[3]
                        },
                    },
                    new AplosApiTagGroupDetail
                    {
                        Tags = new List<AplosApiTagDetail>
                        {
                            expectedAplosTagValues[4],
                            expectedAplosTagValues[5],
                        },
                    },
                },
            };

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<AplosApiTagDetail> actualAplosTagValues = service.GetFlattenedAplosTagValues(aplosTagCategory, default);

            //Assert
            Assert.NotNull(actualAplosTagValues);
            Assert.Equal(expectedAplosTagValues, actualAplosTagValues);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ValidateAplosApiCredentials_ReturnsTrue_WhenApiCredentialsAreValid(bool expectedResult)
        {
            //Arrange
            _mockAplosApiClient.Setup(mockAplosClient => mockAplosClient.GetAndValidateAplosAccessToken(default)).Returns(Task.FromResult(expectedResult));
            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            var result = await service.ValidateAplosApiCredentials(new Pex2AplosMappingModel { AplosClientId = "abc", AplosPrivateKey = "123", }, default);

            //Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void WasPexTransactionSyncedToAplos_ReturnsTrue_WhenMatchedAndNoteAndMemoArePopulated()
        {
            //Arrange
            string pexTransactionId = "12345";

            var aplosTransactions = new List<AplosApiTransactionDetail>
            {
                new AplosApiTransactionDetail
                {
                    Memo = pexTransactionId,
                    Note = pexTransactionId,
                },
                new AplosApiTransactionDetail
                {
                    Memo = "xyz",
                    Note = "xyz",
                },
            };

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            bool wasSynced = service.WasPexTransactionSyncedToAplos(aplosTransactions, pexTransactionId);

            //Assert
            Assert.True(wasSynced);
        }

        [Fact]
        public void WasPexTransactionSyncedToAplos_ReturnsTrue_WhenMatchedAndOnlyMemoIsPopulated()
        {
            //Arrange
            string pexTransactionId = "12345";

            var aplosTransactions = new List<AplosApiTransactionDetail>
            {
                new AplosApiTransactionDetail
                {
                    Memo = pexTransactionId,
                },
                new AplosApiTransactionDetail
                {
                    Memo = "xyz",
                },
            };

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            bool wasSynced = service.WasPexTransactionSyncedToAplos(aplosTransactions, pexTransactionId);

            //Assert
            Assert.True(wasSynced);
        }

        [Fact]
        public void WasPexTransactionSyncedToAplos_ReturnsFalse_WhenNotMatched()
        {
            //Arrange
            string pexTransactionId = "12345";

            var aplosTransactions = new List<AplosApiTransactionDetail>
            {
                new AplosApiTransactionDetail
                {
                    Memo = "abc",
                    Note = "abc",
                },
                new AplosApiTransactionDetail
                {
                    Memo = "xyz",
                    Note = "xyz",
                },
            };

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            bool wasSynced = service.WasPexTransactionSyncedToAplos(aplosTransactions, pexTransactionId);

            //Assert
            Assert.False(wasSynced);
        }

        [Fact]
        public void DedupeAplosAccounts_ReturnsUniqueAccounts_WhenThreeDupesExist()
        {
            //Arrange
            var account1 = new PexAplosApiObject
            {
                Id = "1001",
                Name = "Resources",
            };

            var account2 = new PexAplosApiObject
            {
                Id = "1002",
                Name = "Resources",
            };

            var account3 = new PexAplosApiObject
            {
                Id = "1003",
                Name = "Travel",
            };

            var account4 = new PexAplosApiObject
            {
                Id = "1004",
                Name = "Resources",
            };

            var accounts = new[]
            {
                account1,
                account2,
                account3,
                account4,
            };

            //Act
            PexAplosApiObject[] dedupedAccounts = AplosIntegrationService.DedupeAplosAccounts(accounts).ToArray();

            //Assert
            Assert.NotNull(dedupedAccounts);
            Assert.Equal(accounts.Length, dedupedAccounts.Length);
            Assert.Equal("Resources (1001)", dedupedAccounts[0].Name);
            Assert.Equal("Resources (1002)", dedupedAccounts[1].Name);
            Assert.Equal("Travel", dedupedAccounts[2].Name);
            Assert.Equal("Resources (1004)", dedupedAccounts[3].Name);
        }

        private AplosIntegrationService GetAplosIntegrationService()
        {
            _mockOptions.Setup(mockOptions => mockOptions.Value).Returns(new AppSettingsModel());
            _mockAplosApiClientFactory
                .Setup(mockAplosClientFactory => mockAplosClientFactory.CreateClient(
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
                _mockSettings);
        }

        private Pex2AplosMappingModel GetMapping(
            AplosAuthenticationMode authenticationMode = AplosAuthenticationMode.PartnerAuthentication,
            string clientId = "clientId",
            string privateKey = "privateKey",
            string accountId = "accountId")
        {
            return new Pex2AplosMappingModel
            {
                AplosAuthenticationMode = authenticationMode,
                AplosAccountId = accountId,
                AplosClientId = clientId,
                AplosPrivateKey = privateKey,
            };
        }
    }
}
