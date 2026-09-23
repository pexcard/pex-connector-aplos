using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Const;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using AplosConnector.Web.Controllers;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class MappingControllerTests
    {
        private const int BusinessAcctId = 4242;
        private static readonly Guid SessionGuid = new("6f1f6f2c-1c6f-4d3a-9c2e-7f0a5b3d8e11");

        private readonly Mock<PexOAuthSessionStorage> _sessions = new(MockBehavior.Loose, (TableClient)null);
        private readonly Mock<Pex2AplosMappingStorage> _mappings =
            new(MockBehavior.Loose, (TableClient)null, (IStorageMappingService)null, (ILogger)null);
        private readonly Mock<SyncHistoryStorage> _history = new(MockBehavior.Loose, (TableClient)null);

        private MappingController BuildController() =>
            new(_sessions.Object, _mappings.Object, _history.Object, null, null, null);

        private void SetupSession() =>
            _sessions.Setup(s => s.GetBySessionGuidAsync(SessionGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PexOAuthSessionModel { SessionGuid = SessionGuid, PEXBusinessAcctId = BusinessAcctId });

        private void SetupMapping() =>
            _mappings.Setup(m => m.GetByBusinessAcctIdAsync(BusinessAcctId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Pex2AplosMappingModel { PEXBusinessAcctId = BusinessAcctId });

        private void SetupHistory(params string[] syncTypes) =>
            _history.Setup(h => h.GetByBusiness(BusinessAcctId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SyncResultModel>(
                    Array.ConvertAll(syncTypes, t => new SyncResultModel { SyncType = t })));

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
            SetupSession();
            SetupMapping();
            SetupHistory(SyncTypes.Transactions);

            var result = await BuildController().GetSyncResults(SessionGuid.ToString(), default);

            Assert.Equal(new[] { SyncTypes.Transactions }, result.Value.ConvertAll(r => r.SyncType));
        }

        [Fact]
        public async Task GetSyncResults_RewritesTheLegacyBillPaymentsLabel()
        {
            SetupSession();
            SetupMapping();
            SetupHistory(SyncTypes.LegacyBillPaymentsLabel, SyncTypes.BillPayments, SyncTypes.Transactions);

            var result = await BuildController().GetSyncResults(SessionGuid.ToString(), default);

            Assert.Equal(
                new[] { SyncTypes.PexStatementPayments, SyncTypes.BillPayments, SyncTypes.Transactions },
                result.Value.ConvertAll(r => r.SyncType));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task GetSyncResults_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
            var result = await BuildController().GetSyncResults(sessionId, default);

            Assert.IsType<BadRequestResult>(result.Result);
        }

        [Fact]
        public async Task GetSyncResults_ReturnsUnauthorized_WithNonExistentSessionId()
        {
            _sessions.Setup(s => s.GetBySessionGuidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PexOAuthSessionModel)null);

            var result = await BuildController().GetSyncResults(SessionGuid.ToString(), default);

            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public async Task GetSyncResults_ReturnsNotFound_WithUnmappedSessionId()
        {
            SetupSession();
            _mappings.Setup(m => m.GetByBusinessAcctIdAsync(BusinessAcctId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Pex2AplosMappingModel)null);

            var result = await BuildController().GetSyncResults(SessionGuid.ToString(), default);

            Assert.IsType<NotFoundResult>(result.Result);
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
