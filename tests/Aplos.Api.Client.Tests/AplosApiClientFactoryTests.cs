using Aplos.Api.Client.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Net.Http;
using Xunit;

namespace Aplos.Api.Client.Tests
{
    public class AplosApiClientFactoryTests
    {
        private readonly Mock<IHttpClientFactory> _httpClientFactory;
        private readonly Mock<IAccessTokenDecryptor> _accessTokenDecryptor;
        private readonly Mock<ILogger> _logger;

        public AplosApiClientFactoryTests()
        {
            _httpClientFactory = new Mock<IHttpClientFactory>();
            _accessTokenDecryptor = new Mock<IAccessTokenDecryptor>();
            _logger = new Mock<ILogger>();
        }

        [Fact]
        public void Create_ReturnsAplosApiClient_WithValidInputs()
        {
            //Arrange
            var aplosApiClientFactory = new AplosApiClientFactory(
                _httpClientFactory.Object,
                _accessTokenDecryptor.Object,
                _logger.Object);

            //Act
            IAplosApiClient aplosApiClient = aplosApiClientFactory.CreateClient(
                "acctid",
                "clientid",
                "pk",
                new Uri("https://www.pexcard.com/"),
                null,
                null);

            //Assert
            Assert.NotNull(aplosApiClient);
        }
    }
}
