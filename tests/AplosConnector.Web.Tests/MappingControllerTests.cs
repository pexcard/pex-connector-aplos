using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class MappingControllerTests
    {
        [Fact]
        public async Task DeleteMapping_ReturnsOk_WithValidSessionId()
        {
        }

        [Fact]
        public async Task DeleteMapping_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task DeleteMapping_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task SaveSettings_ReturnsOk_WithValidSessionId()
        {
        }

        [Fact]
        public async Task SaveSettings_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task SaveSettings_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetSettings_ReturnsOk_WithValidSessionId()
        {
        }

        [Fact]
        public async Task GetSettings_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task GetSettings_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetAplosAuthenticationStatus_ReturnsOk_WithValidSessionId()
        {
        }

        [Fact]
        public async Task GetAplosAuthenticationStatus_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task GetAplosAuthenticationStatus_ReturnsUnauthorized_WithNonExistantSessionId()
        {
        }

        [Fact]
        public async Task GetAplosAuthenticationStatus_ReturnsNotFound_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task GetSyncResults_ReturnsOk_WithValidSessionId()
        {
        }

        [Fact]
        public async Task GetSyncResults_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task GetSyncResults_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task GetSyncResults_ReturnsNotFound_WithUnmappedSessionId()
        {
        }

        [Fact]
        public async Task Sync_ReturnsOk_WithValidSessionId()
        {
        }

        [Fact]
        public async Task Sync_ReturnsBadRequest_WithInvalidSessionId()
        {
        }

        [Fact]
        public async Task Sync_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public async Task Sync_ReturnsNotFound_WithUnmappedSessionId()
        {
        }
    }
}
