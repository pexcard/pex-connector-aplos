﻿using Aplos.Api.Client.Abstractions;
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
using Microsoft.Extensions.Options;
using Moq;
using PexCard.Api.Client.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class AplosIntegrationServiceTests
    {
        private readonly Mock<IAplosApiClient> _mockAplosApiClient;

        private readonly Mock<IOptions<AppSettingsModel>> _mockOptions;
        private readonly Mock<IAplosApiClientFactory> _mockAplosApiClientFactory;
        private readonly Mock<IAplosIntegrationMappingService> _mockAplosIntegrationMappingService;
        private readonly Mock<IPexApiClient> _mockPexApiClient;

        public AplosIntegrationServiceTests()
        {
            _mockAplosApiClient = new Mock<IAplosApiClient>();

            _mockOptions = new Mock<IOptions<AppSettingsModel>>();
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
                .Setup(mockAplosClient => mockAplosClient.GetAccount(aplosAccount.AccountNumber))
                .Returns(Task.FromResult(aplosAccountResponse));

            var mappedAplosAccount = new PexAplosApiObject { Id = aplosAccount.AccountNumber.ToString(), Name = aplosAccount.Name, };
            _mockAplosIntegrationMappingService.Setup(mockMappingService => mockMappingService.Map(aplosAccount)).Returns(mappedAplosAccount);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosAccount = await service.GetAplosAccount(new Pex2AplosMappingModel(), aplosAccount.AccountNumber);

            //Assert
            Assert.Equal(mappedAplosAccount, actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosAccount_ReturnsNull_WhenAplosAccountNotFound()
        {
            //Arrange
            var aplosAccountResponse = new AplosApiAccountResponse { Data = new AplosApiAccountData { Account = default, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccount(It.IsAny<decimal>()))
                .Returns(Task.FromResult(aplosAccountResponse));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiAccountDetail>()))
                .Returns(default(PexAplosApiObject));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosAccount = await service.GetAplosAccount(new Pex2AplosMappingModel(), It.IsAny<decimal>());

            //Assert
            Assert.Null(actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosAccounts_ReturnsAplosApiObject_WhenAplosAccountsFound()
        {
            //Arrange
            var aplosAccount = new AplosApiAccountDetail { AccountNumber = 123, Name = "test" };
            var aplosAccounts = new List<AplosApiAccountDetail> {aplosAccount};

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccounts(It.IsAny<string>()))
                .Returns(Task.FromResult(aplosAccounts));

            IEnumerable<PexAplosApiObject> mappedAplosAccount = new[] { new PexAplosApiObject { Id = aplosAccount.AccountNumber.ToString(), Name = aplosAccount.Name, } };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosAccounts))
                .Returns(mappedAplosAccount);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            var actualMappedAplosAccount = await service.GetAplosAccounts(new Pex2AplosMappingModel());

            //Assert
            Assert.Equal(mappedAplosAccount, actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosAccounts_ReturnsNull_WhenAplosAccountsNotFound()
        {
            //Arrange
            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetAccounts(It.IsAny<string>()))
                .Returns(Task.FromResult(default(List<AplosApiAccountDetail>)));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiAccountDetail[]>()))
                .Returns(default(PexAplosApiObject[]));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosAccount = await service.GetAplosAccounts(new Pex2AplosMappingModel());

            //Assert
            Assert.Null(actualMappedAplosAccount);
        }

        [Fact]
        public async Task GetAplosContact_ReturnsAplosApiObject_WhenAplosContactFound()
        {
            //Arrange
            var aplosContact = new AplosApiContactDetail { Id = 123, CompanyName = "test" };
            var aplosContactResponse = new AplosApiContactResponse { Data = new AplosApiContactData { Contact = aplosContact, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContact(aplosContact.Id))
                .Returns(Task.FromResult(aplosContactResponse));

            var mappedAplosContact = new PexAplosApiObject { Id = aplosContact.Id.ToString(), Name = aplosContact.CompanyName, };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosContact))
                .Returns(mappedAplosContact);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosContact = await service.GetAplosContact(new Pex2AplosMappingModel(), aplosContact.Id);

            //Assert
            Assert.Equal(mappedAplosContact, actualMappedAplosContact);
        }

        [Fact]
        public async Task GetAplosContact_ReturnsNull_WhenAplosContactNotFound()
        {
            //Arrange
            var aplosContactResponse = new AplosApiContactResponse { Data = new AplosApiContactData { Contact = default, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContact(It.IsAny<int>()))
                .Returns(Task.FromResult(aplosContactResponse));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiContactDetail>()))
                .Returns(default(PexAplosApiObject));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosContact = await service.GetAplosContact(new Pex2AplosMappingModel(), It.IsAny<int>());

            //Assert
            Assert.Null(actualMappedAplosContact);
        }

        [Fact]
        public async Task GetAplosContacts_ReturnsAplosApiObject_WhenAplosContactsFound()
        {
            //Arrange
            var aplosContact = new AplosApiContactDetail { Id = 123, CompanyName = "test" };
            var aplosContacts = new List<AplosApiContactDetail> {aplosContact};

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContacts())
                .Returns(Task.FromResult(aplosContacts));

            IEnumerable<PexAplosApiObject> mappedAplosContacts = new[] { new PexAplosApiObject { Id = aplosContact.Id.ToString(), Name = aplosContact.CompanyName, } };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosContacts))
                .Returns(mappedAplosContacts);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosContats = await service.GetAplosContacts(new Pex2AplosMappingModel());

            //Assert
            Assert.Equal(mappedAplosContacts, actualMappedAplosContats);
        }

        [Fact]
        public async Task GetAplosContacts_ReturnsNull_WhenAplosContactsNotFound()
        {
            //Arrange
            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetContacts())
                .Returns(Task.FromResult(default(List<AplosApiContactDetail>)));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiContactDetail[]>()))
                .Returns(default(PexAplosApiObject[]));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosContactList = await service.GetAplosContacts(new Pex2AplosMappingModel());

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
                .Setup(mockAplosClient => mockAplosClient.GetFund(aplosFund.Id))
                .Returns(Task.FromResult(aplosFundResponse));

            var mappedAplosFund = new PexAplosApiObject { Id = aplosFund.Id.ToString(), Name = aplosFund.Name, };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosFund))
                .Returns(mappedAplosFund);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosFund = await service.GetAplosFund(new Pex2AplosMappingModel(), aplosFund.Id);

            //Assert
            Assert.Equal(mappedAplosFund, actualMappedAplosFund);
        }

        [Fact]
        public async Task GetAplosFund_ReturnsNull_WhenAplosFundNotFound()
        {
            //Arrange
            var aplosFundResponse = new AplosApiFundResponse { Data = new AplosApiFundData { Fund = default, } };

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFund(It.IsAny<int>()))
                .Returns(Task.FromResult(aplosFundResponse));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiFundDetail>()))
                .Returns(default(PexAplosApiObject));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            PexAplosApiObject actualMappedAplosFund = await service.GetAplosFund(new Pex2AplosMappingModel(), It.IsAny<int>());

            //Assert
            Assert.Null(actualMappedAplosFund);
        }

        [Fact]
        public async Task GetAplosFunds_ReturnsAplosApiObject_WhenAplosFundsFound()
        {
            //Arrange
            var aplosFund = new AplosApiFundDetail { Id = 123, Name = "test" };
            var aplosFunds = new List<AplosApiFundDetail> {aplosFund};

            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFunds())
                .Returns(Task.FromResult(aplosFunds));

            IEnumerable<PexAplosApiObject> mappedAplosFunds = new[] { new PexAplosApiObject { Id = aplosFund.Id.ToString(), Name = aplosFund.Name, } };

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(aplosFunds))
                .Returns(mappedAplosFunds);

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosFunds = await service.GetAplosFunds(new Pex2AplosMappingModel());

            //Assert
            Assert.Equal(mappedAplosFunds, actualMappedAplosFunds);
        }

        [Fact]
        public async Task GetAplosFunds_ReturnsNull_WhenAplosFundsNotFound()
        {
            //Arrange
            _mockAplosApiClient
                .Setup(mockAplosClient => mockAplosClient.GetFunds())
                .Returns(Task.FromResult(default(List<AplosApiFundDetail>)));

            _mockAplosIntegrationMappingService
                .Setup(mockMappingService => mockMappingService.Map(It.IsAny<AplosApiFundDetail[]>()))
                .Returns(default(PexAplosApiObject[]));

            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            IEnumerable<PexAplosApiObject> actualMappedAplosFunds = await service.GetAplosFunds(new Pex2AplosMappingModel());

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
            IEnumerable<AplosApiTagDetail> actualAplosTagValues = service.GetFlattenedAplosTagValues(aplosTagCategory);

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
            _mockAplosApiClient.Setup(mockAplosClient => mockAplosClient.GetAndValidateAplosAccessToken()).Returns(Task.FromResult(expectedResult));
            AplosIntegrationService service = GetAplosIntegrationService();

            //Act
            var validationResult = await service.ValidateAplosApiCredentials(new Pex2AplosMappingModel());

            //Assert
            Assert.Equal(expectedResult, validationResult.CanObtainAccessToken);
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
                    It.IsAny<Func<AplosAuthModel, ILogger, Task>>()))
                .Returns(_mockAplosApiClient.Object);

            return new AplosIntegrationService(
                _mockOptions.Object,
                _mockAplosApiClientFactory.Object,
                _mockAplosIntegrationMappingService.Object,
                _mockPexApiClient.Object,
                null,
                null);
        }
    }
}
