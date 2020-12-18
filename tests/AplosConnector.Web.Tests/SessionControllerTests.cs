using AplosConnector.Common.Models.Settings;
using AplosConnector.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class SessionControllerTests
    {
        [Fact]
        public void CreateAplosToken_ReturnsOk_WithValidSessionIdAndNonExistentSettings()
        {
        }

        [Fact]
        public void CreateAplosToken_ReturnsOk_WithValidSessionIdAndExistingtSettings()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task CreateAplosToken_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
            //Arrange
            var controller = new SessionController(null, null, Options.Create<AppSettingsModel>(null), null, null, null);

            //Act
            var result = await controller.CreateAplosToken(sessionId, null);

            //Assert
            Assert.Equal(typeof(BadRequestResult), result.Result.GetType());
        }

        [Fact]
        public void CreateAplosToken_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public void CreateAplosToken_ReturnsForbidResult_WithValidSessionIdAndInvalidAplosCredentials()
        {
        }

        [Fact]
        public void Validity_ReturnsOkResult_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public void Validity_ReturnsBadRequestResult_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public void PexBusinessName_ReturnsOkResult_WithValidSessionId()
        {
        }

        [Fact]
        public void PexBusinessName_ReturnsUnauthorizedResult_WithNonExistentSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public void PexBusinessName_ReturnsBadRequestResult_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public void DeleteSession_ReturnsOkResult_WithValidSessionIdAndExistingSession()
        {
        }

        [Fact]
        public void DeleteSession_ReturnsOkResult_WithValidSessionIdAndNonExistentSession()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public void DeleteSession_ReturnsBadRequestResult_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public void CreateSession_ReturnsOkResult_WithValidInput()
        {
        }
    }
}
