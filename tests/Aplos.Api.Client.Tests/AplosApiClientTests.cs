using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Aplos.Api.Client.Tests
{
    public class AplosApiClientTests
    {
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly Mock<IAccessTokenDecryptor> _mockAccessTokenDecryptor;
        private readonly Mock<ILogger<AplosApiClientFactory>> _mockLogger;

        public AplosApiClientTests()
        {
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockAccessTokenDecryptor = new Mock<IAccessTokenDecryptor>();
            _mockLogger = new Mock<ILogger<AplosApiClientFactory>>();
        }

        [Fact]
        public async void GetAccounts_ReturnsAccounts_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK,  File.ReadAllText("Samples/Response/GET_auth.json"       )),
                ($"/accounts/",     HttpMethod.Get, HttpStatusCode.OK,  File.ReadAllText("Samples/Response/GET_accounts.json"   )));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            var apiResponse = await aplosApiClient.GetAccounts();

            //Assert
            Assert.NotNull(apiResponse);
            Assert.Equal(2, apiResponse.Count);

            var account1 = apiResponse[0];
            Assert.Equal(1601, account1.AccountNumber);
            Assert.Equal("Ty Test Asset Dev", account1.Name);
            Assert.Equal("asset", account1.Category);
            Assert.True(account1.IsEnabled);
            Assert.Equal("Register", account1.Type);
            Assert.Equal("cash", account1.Activity);

            var account2 = apiResponse[1];
            Assert.Equal(5601.1m, account2.AccountNumber);
            Assert.Equal("Ty Test Expense Dev", account2.Name);
            Assert.Equal("expense", account2.Category);
            Assert.True(account2.IsEnabled);
            Assert.Null(account2.Type);
            Assert.Null(account2.Activity);
        }

        [Fact]
        public async void GetAccount_ReturnsAccount_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/accounts/1601", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_accounts_1601.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            AplosApiAccountResponse apiResponse = await aplosApiClient.GetAccount(1601);

            //Assert
            Assert.NotNull(apiResponse);
            Assert.NotNull(apiResponse.Data);
            Assert.NotNull(apiResponse.Data.Account);

            var account = apiResponse.Data.Account;
            Assert.Equal(1601, account.AccountNumber);
            Assert.Equal("Ty Test Asset Dev", account.Name);
            Assert.Equal("asset", account.Category);
            Assert.True(account.IsEnabled);
            Assert.Equal("Register", account.Type);
            Assert.Equal("cash", account.Activity);
        }

        [Fact]
        public async void GetFunds_ReturnsFunds_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/funds/", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_funds.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            var apiResponse = await aplosApiClient.GetFunds();

            //Assert
            Assert.NotNull(apiResponse);
            Assert.Equal(3, apiResponse.Count);

            var fund1 = apiResponse[0];
            Assert.Equal(225755, fund1.Id);
            Assert.Equal("General Fund", fund1.Name);
            Assert.Equal("General Fund - Fund Balance", fund1.BalanceAccountName);
            Assert.Equal(3000, fund1.BalanceAccountNumber);

            var fund2 = apiResponse[1];
            Assert.Equal(225756, fund2.Id);
            Assert.Equal("Building Fund", fund2.Name);
            Assert.Equal("Building Fund - Fund Balance", fund2.BalanceAccountName);
            Assert.Equal(3100, fund2.BalanceAccountNumber);

            var fund3 = apiResponse[2];
            Assert.Equal(225793, fund3.Id);
            Assert.Equal("Allocate Fund", fund3.Name);
            Assert.Equal("Allocate Fund - Fund Balance", fund3.BalanceAccountName);
            Assert.Equal(3101, fund3.BalanceAccountNumber);
        }

        [Fact]
        public async void GetFund_ReturnsFund_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/funds/225755", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_funds_225755.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            AplosApiFundResponse apiResponse = await aplosApiClient.GetFund(225755);

            //Assert
            Assert.NotNull(apiResponse);
            Assert.NotNull(apiResponse.Data);
            Assert.NotNull(apiResponse.Data.Fund);

            var fund = apiResponse.Data.Fund;
            Assert.Equal(225755, fund.Id);
            Assert.Equal("General Fund", fund.Name);
            Assert.Equal("General Fund - Fund Balance", fund.BalanceAccountName);
            Assert.Equal(3000, fund.BalanceAccountNumber);
        }

        [Fact]
        public async void GetContacts_ReturnsContacts_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/contacts/", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_contacts.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            var apiResponse = await aplosApiClient.GetContacts();

            //Assert
            Assert.NotNull(apiResponse);
            Assert.Equal(2, apiResponse.Count);

            var contact1 = apiResponse[0];
            Assert.Equal(5719509, contact1.Id);
            Assert.Equal("Ty", contact1.FirstName);
            Assert.Equal("Baker", contact1.LastName);
            Assert.Equal("individual", contact1.Type);
            //Assert.Equal("chuck@thecheese.com", contact1.Email);

            var contact2 = apiResponse[1];
            Assert.Equal(5666276, contact2.Id);
            Assert.Equal("Chuck E. Cheese", contact2.CompanyName);
            Assert.Equal("company", contact2.Type);
            Assert.Equal("chuck@thecheese.com", contact2.Email);
        }

        [Fact]
        public async void GetContact_ReturnsCompanyContact_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/contacts/5666276", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_contacts_5666276.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            AplosApiContactResponse apiResponse = await aplosApiClient.GetContact(5666276);

            //Assert
            Assert.NotNull(apiResponse);
            Assert.NotNull(apiResponse.Data);
            Assert.NotNull(apiResponse.Data.Contact);

            AplosApiContactDetail contact = apiResponse.Data.Contact;
            Assert.Equal(5666276, contact.Id);
            Assert.Equal("Chuck E. Cheese", contact.CompanyName);
            Assert.Equal("company", contact.Type);
            Assert.Equal("chuck@thecheese.com", contact.Email);

            AplosApiContactEmailDetail[] emails = contact.Emails;
            Assert.NotNull(emails);
            AplosApiContactEmailDetail email1 = emails[0];
            Assert.NotNull(email1);
            Assert.Equal("Work", email1.Name);
            Assert.Equal("chuck@thecheese.com", email1.Address);
            Assert.True(email1.IsPrimary);

            AplosApiContactPhoneDetail[] phones = contact.Phones;
            Assert.NotNull(phones);
            var phone1 = phones[0];
            Assert.NotNull(phone1);
            Assert.Equal("Work", phone1.Name);
            Assert.Equal("2129470100", phone1.TelephoneNumber);
            Assert.True(phone1.IsPrimary);

            Assert.Null(contact.Addresses);
        }

        [Fact]
        public async void GetTransactions_ReturnsTransactions_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/transactions/", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_transactions.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            var apiResponse = await aplosApiClient.GetTransactions(DateTime.Parse("2020-01-13"));

            //Assert
            Assert.NotNull(apiResponse);
            Assert.Equal(2, apiResponse.Count);

            AplosApiTransactionDetail transaction1 = apiResponse[0];
            Assert.Equal(15799496, transaction1.Id);
            Assert.Equal("416294307 | Ty Baker", transaction1.Note);
            Assert.Equal(DateTime.Parse("2019-12-12"), transaction1.Date);
            Assert.Equal(15.87m, transaction1.Amount);

            AplosApiContactDetail contact1 = transaction1.Contact;
            Assert.NotNull(contact1);
            Assert.Equal(5791245, contact1.Id);
            Assert.Equal("Dec 17 Test", contact1.CompanyName);
            Assert.Equal("company", contact1.Type);

            AplosApiTransactionDetail transaction2 = apiResponse[1];
            Assert.Equal(15799495, transaction2.Id);
            Assert.Equal("416294302 | Ty Baker", transaction2.Note);
            Assert.Equal(DateTime.Parse("2019-12-12"), transaction2.Date);
            Assert.Equal(26.91m, transaction2.Amount);

            AplosApiContactDetail contact2 = transaction2.Contact;
            Assert.NotNull(contact2);
            Assert.Equal(5708502, contact2.Id);
            Assert.Equal("Some Payee", contact2.CompanyName);
            Assert.Equal("company", contact2.Type);
        }

        [Fact]
        public async void GetTransaction_ReturnsTransaction_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/transactions/15145647", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_transactions_15145647.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            AplosApiTransactionResponse apiResponse = await aplosApiClient.GetTransaction(15145647);

            //Assert
            Assert.NotNull(apiResponse);
            Assert.NotNull(apiResponse.Data);
            Assert.NotNull(apiResponse.Data.Transaction);

            AplosApiTransactionDetail transaction = apiResponse.Data.Transaction;
            Assert.Equal(15145647, transaction.Id);
            Assert.Equal("Test 123", transaction.Note);
            Assert.Equal(DateTime.Parse("2019-11-25"), transaction.Date);
            Assert.Equal(50, transaction.Amount);

            AplosApiContactDetail contact = transaction.Contact;
            Assert.NotNull(contact);
            Assert.Equal(5656815, contact.Id);
            Assert.Equal("Lumber", contact.CompanyName);
            Assert.Equal("company", contact.Type);

            AplosApiTransactionLineDetail[] lines = transaction.Lines;
            Assert.NotNull(lines);
            Assert.Equal(2, lines.Length);

            AplosApiTransactionLineDetail line1 = lines[0];
            Assert.NotNull(line1);
            Assert.Equal(75372351, line1.Id);
            Assert.Equal(50.00m, line1.Amount);
            Assert.NotNull(line1.Account);
            Assert.Equal(1200, line1.Account.AccountNumber);
            Assert.Equal("PEX Account", line1.Account.Name);
            Assert.NotNull(line1.Fund);
            Assert.Equal(225755, line1.Fund.Id);
            Assert.Equal("General Fund", line1.Fund.Name);

            AplosApiTransactionLineDetail line2 = lines[1];
            Assert.NotNull(line2);
            Assert.Equal(75372350, line2.Id);
            Assert.Equal(-50.00m, line2.Amount);
            Assert.NotNull(line2.Account);
            Assert.Equal(1000, line2.Account.AccountNumber);
            Assert.Equal("Checking", line2.Account.Name);
            Assert.NotNull(line2.Fund);
            Assert.Equal(225755, line2.Fund.Id);
            Assert.Equal("General Fund", line2.Fund.Name);
        }

        [Fact]
        public async void CreateTransaction_CratesTransaction_WithValidRequest()
        {
        }

        [Fact]
        public async void CreateTransaction_ReturnsError_WithHttp422Response()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/transactions/", HttpMethod.Post, HttpStatusCode.UnprocessableEntity, JsonConvert.SerializeObject(new AplosApiErrorResponse { Status = (int)HttpStatusCode.UnprocessableEntity, })));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            Func<Task<AplosApiTransactionResponse>> aplosApiResponse = () => aplosApiClient.CreateTransaction(new AplosApiTransactionDetail());

            //Assert
            var exception = await Assert.ThrowsAsync<AplosApiException>(aplosApiResponse);
            Assert.Equal((int)HttpStatusCode.UnprocessableEntity, exception.AplosApiError.Status);
        }

        [Fact]
        public void GetAplosAccessToken_ReturnsAccessToken_WithNoInitializationAndNoCallbacks()
        {

        }

        [Fact]
        public void GetAplosAccessToken_ReturnsAccessToken_WithNoInitializationAndCallbacks()
        {

        }

        [Fact]
        public void GetAplosAccessToken_ReturnsAccessToken_WithInitializationAndNoCallbacks()
        {

        }

        [Fact]
        public void GetAplosAccessToken_ReturnsAccessToken_WithInitializationAndCallbacks()
        {

        }

        [Fact]
        public void GetAndValidateAplosAccessToken_ReturnsAccessToken_WithInitializationAndCallbacks()
        {

        }

        [Fact]
        public async void GetTags_ReturnsTags_WithValidRequest()
        {
            //Arrange
            var messageHandler = new MockHttpMessageHandler(
                ($"/auth/clientid", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json")),
                ($"/tags/", HttpMethod.Get, HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_tags.json")));

            var httpClient = new HttpClient(messageHandler);

            _mockHttpClientFactory.Setup(mockFactory => mockFactory.CreateClient("")).Returns(httpClient);

            var aplosApiClient = new AplosApiClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                _mockHttpClientFactory.Object,
                _mockAccessTokenDecryptor.Object,
                _mockLogger.Object,
                null,
                null);

            //Act
            var apiResponse = await aplosApiClient.GetTags();

            //Assert
            Assert.NotNull(apiResponse);
            Assert.Equal(2, apiResponse.Count);

            var tagCategory1 = apiResponse[0];
            Assert.NotNull(tagCategory1);
            Assert.Equal("87401", tagCategory1.Id);
            Assert.Equal("Custom", tagCategory1.Name);
            Assert.NotNull(tagCategory1.TagGroups);
            Assert.Equal(4, tagCategory1.TagGroups.Count);
            {
                var tagCategory1Group1 = tagCategory1.TagGroups[0];
                Assert.NotNull(tagCategory1Group1);
                Assert.Equal("89292", tagCategory1Group1.Id);
                Assert.Equal("All Tags", tagCategory1Group1.Name); 
                Assert.NotNull(tagCategory1Group1.Tags);
                Assert.Equal(2, tagCategory1Group1.Tags.Count);
                {
                    var tagCategory1Group1Tag1 = tagCategory1Group1.Tags[0];
                    Assert.Equal("91258", tagCategory1Group1Tag1.Id);
                    Assert.Equal("Allocate", tagCategory1Group1Tag1.Name);
                    Assert.Null(tagCategory1Group1Tag1.SubTags);

                    var tagCategory1Group1Tag2 = tagCategory1Group1.Tags[1];
                    Assert.Equal("120732", tagCategory1Group1Tag2.Id);
                    Assert.Equal("PEX Test", tagCategory1Group1Tag2.Name);
                    Assert.Null(tagCategory1Group1Tag2.SubTags);
                }

                var tagCategory1Group2 = tagCategory1.TagGroups[1];
                Assert.NotNull(tagCategory1Group2);
                Assert.Equal("110220", tagCategory1Group2.Id);
                Assert.Equal("PEX Group Dev", tagCategory1Group2.Name);
                Assert.NotNull(tagCategory1Group2.Tags);
                Assert.Equal(0, tagCategory1Group2.Tags.Count);

                var tagCategory1Group3 = tagCategory1.TagGroups[2];
                Assert.NotNull(tagCategory1Group3);
                Assert.Equal("110221", tagCategory1Group3.Id);
                Assert.Equal("PEX Group QA", tagCategory1Group3.Name);
                Assert.NotNull(tagCategory1Group3.Tags);
                Assert.Equal(3, tagCategory1Group3.Tags.Count);
                {
                    var tagCategory1Group3Tag1 = tagCategory1Group3.Tags[0];
                    Assert.Equal("120733", tagCategory1Group3Tag1.Id);
                    Assert.Equal("PEX Test QA 1", tagCategory1Group3Tag1.Name);
                    Assert.NotNull(tagCategory1Group3Tag1.SubTags);
                    Assert.Equal(2, tagCategory1Group3Tag1.SubTags.Count);
                    {
                        var tagCategory1Group3Tag2SubTag1 = tagCategory1Group3Tag1.SubTags[0];
                        Assert.Equal("120735", tagCategory1Group3Tag2SubTag1.Id);
                        Assert.Equal("PEX Test QA 1.1", tagCategory1Group3Tag2SubTag1.Name);
                        Assert.Null(tagCategory1Group3Tag2SubTag1.SubTags);

                        var tagCategory1Group3Tag2SubTag2 = tagCategory1Group3Tag1.SubTags[1];
                        Assert.Equal("120736", tagCategory1Group3Tag2SubTag2.Id);
                        Assert.Equal("PEX Test QA 1.2", tagCategory1Group3Tag2SubTag2.Name);
                        Assert.Null(tagCategory1Group3Tag2SubTag2.SubTags);
                    }

                    var tagCategory1Group1Tag2 = tagCategory1Group3.Tags[1];
                    Assert.Equal("120734", tagCategory1Group1Tag2.Id);
                    Assert.Equal("PEX Test QA 2", tagCategory1Group1Tag2.Name);
                    Assert.Null(tagCategory1Group1Tag2.SubTags);

                    var tagCategory1Group1Tag3 = tagCategory1Group3.Tags[2];
                    Assert.Equal("120739", tagCategory1Group1Tag3.Id);
                    Assert.Equal("Hi I'm Disabled", tagCategory1Group1Tag3.Name);
                    Assert.Null(tagCategory1Group1Tag3.SubTags);
                }

                var tagCategory1Group4 = tagCategory1.TagGroups[3];
                Assert.NotNull(tagCategory1Group4);
                Assert.Equal("110222", tagCategory1Group4.Id);
                Assert.Equal("PEX Group Prod", tagCategory1Group4.Name);
                Assert.NotNull(tagCategory1Group4.Tags);
                Assert.Equal(0, tagCategory1Group4.Tags.Count);
            }

            var tagCategory2 = apiResponse[1];
            Assert.NotNull(tagCategory2);
            Assert.Equal("87404", tagCategory2.Id);
            Assert.Equal("Departments", tagCategory2.Name);
            Assert.Equal("advanced", tagCategory2.Type);
            Assert.NotNull(tagCategory2.TagGroups);
            Assert.Equal(2, tagCategory2.TagGroups.Count);
            {
                var tagCategory2Group1 = tagCategory2.TagGroups[0];
                Assert.Equal("89295", tagCategory2Group1.Id);
                Assert.Equal("PEX Department", tagCategory2Group1.Name);
                Assert.NotNull(tagCategory2Group1.Tags);
                Assert.Equal(2, tagCategory2Group1.Tags.Count);
                {
                    var tagCategory2Group1Tag1 = tagCategory2Group1.Tags[0];
                    Assert.Equal("91261", tagCategory2Group1Tag1.Id);
                    Assert.Equal("Technology", tagCategory2Group1Tag1.Name);
                    Assert.Null(tagCategory2Group1Tag1.SubTags);

                    var tagCategory1Group1Tag2 = tagCategory2Group1.Tags[1];
                    Assert.Equal("120737", tagCategory1Group1Tag2.Id);
                    Assert.Equal("Operations", tagCategory1Group1Tag2.Name);
                    Assert.Null(tagCategory1Group1Tag2.SubTags);
                }

                var tagCategory2Group2 = tagCategory2.TagGroups[1];
                Assert.Equal("892952", tagCategory2Group2.Id);
                Assert.Equal("PEX Department2", tagCategory2Group2.Name);
                Assert.NotNull(tagCategory2Group2.Tags);
                Assert.Equal(1, tagCategory2Group2.Tags.Count);
                {
                    var tagCategory2Group2Tag1 = tagCategory2Group2.Tags[0];
                    Assert.Equal("91299", tagCategory2Group2Tag1.Id);
                    Assert.Equal("Sales", tagCategory2Group2Tag1.Name);
                    Assert.Null(tagCategory2Group2Tag1.SubTags);
                }
            }
        }
    }

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<(string endpoint, HttpMethod httpMethod, HttpStatusCode responseCode, string responseBody)> _config;

        public MockHttpMessageHandler(params (string endpoint, HttpMethod httpMethod, HttpStatusCode responseCode, string responseBody)[] config)
        {
            _config = config.ToList();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //Find a response to return based on the method and URI.
            var matchingConfig = _config.FirstOrDefault(config => config.httpMethod == request.Method && config.endpoint == request.RequestUri.AbsolutePath);

            if (matchingConfig == default)
            {
                throw new InvalidOperationException($"Unable to locate a response for '{request.Method} {request.RequestUri.AbsolutePath}'.");
            }

            //_config.Remove(matchingConfig);

            var response = new HttpResponseMessage();
            response.StatusCode = matchingConfig.responseCode;
            response.Content = new StringContent(
                matchingConfig.responseBody,
                Encoding.UTF8,
                "application/json");

            return response;
        }
    }
}
