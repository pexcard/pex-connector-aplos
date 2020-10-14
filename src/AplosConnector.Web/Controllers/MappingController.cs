﻿using Microsoft.AspNetCore.Mvc;
using AplosConnector.Common.Models;
using AplosConnector.Core.Storages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using AplosConnector.Common.Models.Response;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public class MappingController : Controller
    {
        private readonly PexOAuthSessionStorage _pexOAuthSessionStorage;
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly SyncResultStorage _syncResultStorage;
        private readonly Pex2AplosMappingQueue _mappingQueue;

        public MappingController(
            PexOAuthSessionStorage pexOAuthSessionStorage,
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            SyncResultStorage syncResultStorage,
            Pex2AplosMappingQueue mappingQueue)
        {
            _pexOAuthSessionStorage = pexOAuthSessionStorage;
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _syncResultStorage = syncResultStorage;
            _mappingQueue = mappingQueue;
        }

        [HttpDelete, Route("")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> DeleteMapping(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            await _pex2AplosMappingStorage.DeleteAsync(session.PEXBusinessAcctId);

            return Ok();
        }

        [HttpPut, Route("Settings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SaveSettings(string sessionId, [FromBody]MappingSettingsModel settings)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            var pexAcctId = session.PEXBusinessAcctId;

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(pexAcctId);
            mapping.UpdateFromSettings(settings);

            await _pex2AplosMappingStorage.UpdateAsync(mapping);

            return Ok();
        }

        [HttpGet, Route("Settings")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<MappingSettingsModel>> GetSettings(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId);

            return mapping?.ToStorageModel();
        }

        [HttpGet, Route("AplosServiceAccount")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<AplosServiceAccountModel>> GetAplosServiceAccountData(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId);
            var result = new AplosServiceAccountModel
            {
                //TODO: What is the purpose of /AplosServiceAccount?
                //ClientId = mapping?.AplosAccessToken,
                //ClientSecret = mapping?.AplosRefreshToken
            };

            return result;
        }

        [HttpGet, Route("AplosAuthenticationStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAplosAuthenticationStatus(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId);
            if (mapping == null) return NotFound();

            //TODO: TODO: Move validation logic out of controller
            if (string.IsNullOrWhiteSpace(mapping.AplosClientId) || string.IsNullOrWhiteSpace(mapping.AplosPrivateKey)) return NotFound();

            return Ok();
        }

        [HttpGet, Route("SyncResults")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<List<SyncResultModel>>> GetSyncResults(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId);
            if (mapping == null) return NotFound();

            var result = await _syncResultStorage.GetByBusiness(mapping.PEXBusinessAcctId);
            return result;
        }

        [HttpPost, Route("Sync")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Sync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId);
            if (mapping == null) return NotFound();

            await _mappingQueue.EnqueueMapping(mapping);
            return Ok();
        }
    }
}
