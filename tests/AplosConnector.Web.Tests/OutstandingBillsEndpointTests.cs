using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using AplosConnector.Web.Controllers;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
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

        [Fact]
        public async Task GetOutstandingBills_PassesTheQueryDateThroughAsTheCalendarDay()
        {
            var sessionGuid = Guid.NewGuid();
            var sessions = new Mock<PexOAuthSessionStorage>(MockBehavior.Loose, (TableClient)null);
            sessions.Setup(s => s.GetBySessionGuidAsync(sessionGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PexOAuthSessionModel { SessionGuid = sessionGuid, PEXBusinessAcctId = 4242 });
            var mappings = new Mock<Pex2AplosMappingStorage>(MockBehavior.Loose, (TableClient)null, (IStorageMappingService)null, (ILogger)null);
            mappings.Setup(m => m.GetByBusinessAcctIdAsync(4242, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Pex2AplosMappingModel { PEXBusinessAcctId = 4242 });
            var service = new Mock<IAplosIntegrationService>();
            service.Setup(s => s.GetAplosOutstandingBills(It.IsAny<Pex2AplosMappingModel>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<AplosOutstandingBillModel>());

            var controller = new AplosController(sessions.Object, mappings.Object, service.Object, null);
            await controller.GetOutstandingBills(sessionGuid.ToString(), new DateOnly(2026, 1, 15), default);

            service.Verify(s => s.GetAplosOutstandingBills(It.IsAny<Pex2AplosMappingModel>(), new DateOnly(2026, 1, 15), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
