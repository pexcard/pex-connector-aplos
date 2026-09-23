using AplosConnector.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class OutstandingBillsEndpointTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetOutstandingBills_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
            var controller = new AplosController(null, null, null, null);

            var result = await controller.GetOutstandingBills(sessionId, null, default);

            Assert.IsType<BadRequestResult>(result.Result);
        }
    }
}
