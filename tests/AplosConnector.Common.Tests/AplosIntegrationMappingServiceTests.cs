using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AplosConnector.Common.Tests
{
    public class AplosIntegrationMappingServiceTests
    {
        [Fact]
        public void MapAccount_ReturnsPexAplosApiObject_WithNonNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiAccountDetail aplosAccount = new AplosApiAccountDetail { AccountNumber = 123, Name = "General Fund", };

            //Act
            PexAplosApiObject mappedAplosAccount = service.Map(aplosAccount);

            //Assert
            Assert.NotNull(mappedAplosAccount);
            Assert.Equal(aplosAccount.AccountNumber.ToString(), mappedAplosAccount.Id);
            Assert.Equal(aplosAccount.Name, mappedAplosAccount.Name);
        }

        [Fact]
        public void MapAccount_ReturnsNull_WithNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiAccountDetail aplosAccount = default;

            //Act
            PexAplosApiObject mappedAplosAccount = service.Map(aplosAccount);

            //Assert
            Assert.Null(mappedAplosAccount);
        }

        [Fact]
        public void MapAccounts_ReturnsPexAplosApiObject_WithNonNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiAccountDetail[] aplosAccounts = new[] { new AplosApiAccountDetail { AccountNumber = 123, Name = "General Fund", } };

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosAccounts = service.Map(aplosAccounts);

            //Assert
            Assert.NotNull(mappedAplosAccounts);

            PexAplosApiObject mappedAplosAccount = mappedAplosAccounts.SingleOrDefault();
            AplosApiAccountDetail aplosAccount = aplosAccounts[0];

            Assert.Equal(aplosAccount.AccountNumber.ToString(), mappedAplosAccount.Id);
            Assert.Equal(aplosAccount.Name, mappedAplosAccount.Name);
        }

        [Fact]
        public void MapAccounts_ReturnsNull_WithNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiAccountDetail[] aplosAccounts = default;

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosAccounts = service.Map(aplosAccounts);

            //Assert
            Assert.Null(mappedAplosAccounts);
        }

        [Fact]
        public void MapContact_ReturnsPexAplosApiObject_WithNonNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiContactDetail aplosContact = new AplosApiContactDetail { Id = 123, CompanyName = "General Fund", };

            //Act
            PexAplosApiObject mappedAplosContact = service.Map(aplosContact);

            //Assert
            Assert.NotNull(mappedAplosContact);
            Assert.Equal(aplosContact.Id.ToString(), mappedAplosContact.Id);
            Assert.Equal(aplosContact.CompanyName, mappedAplosContact.Name);
        }

        [Fact]
        public void MapContact_ReturnsNull_WithNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiContactDetail aplosContact = default;

            //Act
            PexAplosApiObject mappedAplosContact = service.Map(aplosContact);

            //Assert
            Assert.Null(mappedAplosContact);
        }

        [Fact]
        public void MapContacts_ReturnsPexAplosApiObject_WithNonNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiContactDetail[] aplosContacts = new[] { new AplosApiContactDetail { Id = 123, CompanyName = "General Fund", } };

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosContacts = service.Map(aplosContacts);

            //Assert
            Assert.NotNull(mappedAplosContacts);

            PexAplosApiObject mappedAplosContact = mappedAplosContacts.SingleOrDefault();
            AplosApiContactDetail aplosContact = aplosContacts[0];

            Assert.Equal(aplosContact.Id.ToString(), mappedAplosContact.Id);
            Assert.Equal(aplosContact.CompanyName, mappedAplosContact.Name);
        }

        [Fact]
        public void MapContacts_ReturnsNull_WithNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiContactDetail[] aplosContacts = default;

            //Act
            IEnumerable<PexAplosApiObject> appedAplosContacts = service.Map(aplosContacts);

            //Assert
            Assert.Null(appedAplosContacts);
        }

        [Fact]
        public void MapFund_ReturnsPexAplosApiObject_WithNonNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiFundDetail aplosFund = new AplosApiFundDetail { Id = 123, Name = "General Fund", };

            //Act
            PexAplosApiObject mappedAplosFund = service.Map(aplosFund);

            //Assert
            Assert.NotNull(mappedAplosFund);
            Assert.Equal(aplosFund.Id.ToString(), mappedAplosFund.Id);
            Assert.Equal(aplosFund.Name, mappedAplosFund.Name);
        }

        [Fact]
        public void MapFund_ReturnsNull_WithNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiFundDetail aplosFund = default;

            //Act
            PexAplosApiObject mappedAplosFund = service.Map(aplosFund);

            //Assert
            Assert.Null(mappedAplosFund);
        }

        [Fact]
        public void MapFunds_ReturnsPexAplosApiObject_WithNonNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiFundDetail[] aplosFunds = new[] { new AplosApiFundDetail { Id = 123, Name = "General Fund", } };

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosFunds = service.Map(aplosFunds);

            //Assert
            Assert.NotNull(mappedAplosFunds);

            PexAplosApiObject mappedAplosFund = mappedAplosFunds.SingleOrDefault();
            AplosApiFundDetail aplosFund = aplosFunds[0];

            Assert.Equal(aplosFund.Id.ToString(), mappedAplosFund.Id);
            Assert.Equal(aplosFund.Name, mappedAplosFund.Name);
        }

        [Fact]
        public void MapFunds_ReturnsNullt_WithNullInput()
        {
            //Arrange
            AplosIntegrationMappingService service = GetMappingService();
            AplosApiFundDetail[] aplosFunds = default;

            //Act
            IEnumerable<PexAplosApiObject> mappedAplosFunds = service.Map(aplosFunds);

            //Assert
            Assert.Null(mappedAplosFunds);
        }

        private AplosIntegrationMappingService GetMappingService()
        {
            return new AplosIntegrationMappingService();
        }
    }
}
