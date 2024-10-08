﻿using Microsoft.AspNetCore.Mvc;
using AplosConnector.Common.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Models.Settings;
using Microsoft.Extensions.Options;
using AplosConnector.Web.Models;
using System.Threading;
using AplosConnector.Common.Storage;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public partial class MappingController : Controller
    {
        private readonly PexOAuthSessionStorage _pexOAuthSessionStorage;
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly SyncHistoryStorage _syncHistoryStorage;
        private readonly Pex2AplosMappingQueue _mappingQueue;
        private readonly IAplosIntegrationService _aplosIntegrationService;
        private readonly AppSettingsModel _appSettings;

        public MappingController(
            PexOAuthSessionStorage pexOAuthSessionStorage,
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            SyncHistoryStorage syncHistoryStorage,
            Pex2AplosMappingQueue mappingQueue,
            IAplosIntegrationService aplosIntegrationService,
            IOptions<AppSettingsModel> appSettings)
        {
            _pexOAuthSessionStorage = pexOAuthSessionStorage;
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _syncHistoryStorage = syncHistoryStorage;
            _mappingQueue = mappingQueue;
            _aplosIntegrationService = aplosIntegrationService;
            _appSettings = appSettings?.Value;
        }

        [HttpDelete, Route("")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> DeleteMapping(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            await _pex2AplosMappingStorage.DeleteAsync(session.PEXBusinessAcctId, cancellationToken);

            return Ok();
        }

        [HttpPut, Route("Settings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SaveSettings(string sessionId, [FromBody] MappingSettingsModel settings, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var pexAcctId = session.PEXBusinessAcctId;

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(pexAcctId, cancellationToken);
            mapping.UpdateFromSettings(settings);

            await _aplosIntegrationService.UpdateFundingSource(mapping, cancellationToken);

            await _pex2AplosMappingStorage.UpdateAsync(mapping, cancellationToken);

            return Ok();
        }

        [HttpGet, Route("Settings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<MappingSettingsModel>> GetSettings(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);

            await _aplosIntegrationService.UpdateFundingSource(mapping, cancellationToken);

            return mapping.ToStorageModel();
        }

        [HttpGet, Route("AplosAuthenticationStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AplosAuthenticationStatusModel>> GetAplosAuthenticationStatus(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _aplosIntegrationService.EnsureMappingInstalled(session, cancellationToken);

            var result = new AplosAuthenticationStatusModel
            {
                AplosAuthenticationMode = mapping.AplosAuthenticationMode,
                HasAplosAccountId = !string.IsNullOrWhiteSpace(mapping.AplosAccountId),
            };

            if (result.AplosAuthenticationMode == AplosAuthenticationMode.PartnerAuthentication && result.HasAplosAccountId)
            {
                result.PartnerVerificationUrl = _appSettings.AplosPartnerVerificationUrl.ToString();
            }

            var isAuthenticated = await _aplosIntegrationService.ValidateAplosApiCredentials(mapping, cancellationToken);
            result.IsAuthenticated = isAuthenticated;

            return Ok(result);
        }

        [HttpGet, Route("SyncResults")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<List<SyncResultModel>>> GetSyncResults(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var result = await _syncHistoryStorage.GetByBusiness(mapping.PEXBusinessAcctId, cancellationToken);
            return result;
        }

        [HttpPost, Route("Sync")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Sync(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            if (!mapping.IsSyncing)
            {
                mapping.IsManualSync = true;

                await Task.Factory.StartNew(async () => await _aplosIntegrationService.Sync(mapping, CancellationToken.None));

                //await _mappingQueue.EnqueueMapping(mapping, CancellationToken.None);
            }

            return Ok();
        }

        [HttpGet, Route("Settings/VendorCardMapping")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetVendorCardMapping(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null)
            {
                return NotFound();
            }

            return Ok(mapping.MapVendorCards);
        }

        [HttpPut, Route("Settings/VendorCardMapping")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SetVendorCardMapping(string sessionId, [FromBody] bool enable, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null)
            {
                return NotFound();
            }

            mapping.MapVendorCards = enable;

            await _pex2AplosMappingStorage.UpdateAsync(mapping, cancellationToken);

            return Ok();
        }
    }
}
