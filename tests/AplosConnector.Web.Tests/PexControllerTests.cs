using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class PexControllerTests
    {
        [Fact]
        public async Task Validity_ReturnsOk_WithValidSessionIdAndPexTagsEnabled()
        {
        }

        [Fact]
        public async Task Validity_ReturnsForbidden_WithValidSessionIdAndPexTagsDisabled()
        {
        }

        [Fact]
        public async Task Validity_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task Validity_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task Validity_ReturnsNotFound_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task Validity_ReturnsUnauthorized_WithNullUserInfoFromPex()
        {
        }

        [Fact]
        public async Task GetTags_ReturnsOk_WithValidSessionI()
        {
        }

        [Fact]
        public async Task GetTags_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task GetTags_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetTags_ReturnsNotFound_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetTags_ReturnsUnauthorized_WithNullUserInfoFromPex()
        {
        }
    }
}
