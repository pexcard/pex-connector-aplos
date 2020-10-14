using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class AplosControllerTests
    {
        [Fact]
        public async Task GetAccounts_ReturnsOk_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetAccounts_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public async Task GetAccounts_ReturnUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetAccounts_ReturnUnauthorized_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetAccount_ReturnsOk_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetAccount_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public async Task GetAccount_ReturnUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetAccount_ReturnUnauthorized_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetContacts_ReturnsOk_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetContacts_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public async Task GetContacts_ReturnUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetContacts_ReturnUnauthorized_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetContact_ReturnsOk_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetContact_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public async Task GetContact_ReturnUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetContact_ReturnUnauthorized_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetFunds_ReturnsOk_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetFunds_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public async Task GetFunds_ReturnUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetFunds_ReturnUnauthorized_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetFund_ReturnsOk_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetFund_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public async Task GetFund_ReturnUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetFund_ReturnUnauthorized_WithUnmappedSessionId()
        {
        }
    }
}
