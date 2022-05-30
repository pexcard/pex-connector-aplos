using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AplosConnector.Common.Models.Response;
using AplosConnector.Core.Storages;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Models;
using System.Threading;
using AplosConnector.Common.Models;
using Microsoft.Extensions.Logging;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public class PexController : Controller
    {
        private readonly IPexApiClient _pexApiClient;
        private readonly PexOAuthSessionStorage _pexOAuthSessionStorage;
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly ILogger<PexController> _logger;

        public PexController(
            IPexApiClient pexApiClient,
            PexOAuthSessionStorage pexOAuthSessionStorage,
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            ILogger<PexController> logger)
        {
            _pexApiClient = pexApiClient;
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _logger = logger;
            _pexOAuthSessionStorage = pexOAuthSessionStorage;
        }

        [HttpGet, Route("Validity")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PexValidityModel>> Validity(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var isTagsEnabled = await _pexApiClient.IsTagsEnabled(mapping.PEXExternalAPIToken, cancellationToken);

            var result = new PexValidityModel
            {
                UseTagsEnabled = isTagsEnabled
            };

            return result.IsValid ? Ok(result) : StatusCode(StatusCodes.Status403Forbidden, result);
        }

        [HttpGet, Route("Tags")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<List<TagDetailsModel>>> GetTags(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var mappingData =
                await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);

            var tags = await _pexApiClient.GetTags(mappingData.PEXExternalAPIToken, cancellationToken);
            var result = tags.OrderBy(t => t.Order).ToList();

            return result;
        }

        [HttpGet, Route("AuthenticationStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAuthenticationStatus(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            try
            {
                await _pexApiClient.GetToken(mapping.PEXExternalAPIToken, cancellationToken);
            }
            catch (Exception e)
            {
                var token = mapping.PEXExternalAPIToken?[..4];
                _logger.LogWarning(e, $"Invalid token {token} for BusinessAccountId: {session.PEXBusinessAcctId}.");
                return Forbid();
            }

            return Ok();
        }

        [HttpPost, Route("UpdatePexAccountLinked")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdatePexAccountLinked(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            try
            {
                var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
                var userProfile = await _pexApiClient.GetMyAdminProfile(session.ExternalToken);

                mapping.PEXExternalAPIToken = session.ExternalToken;
                mapping.PEXEmailAccount = userProfile?.Admin?.Email;
                mapping.PEXNameAccount = $"{userProfile?.Admin?.FirstName} {userProfile?.Admin?.LastName}";

                await _pex2AplosMappingStorage.UpdateAsync(mapping, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, $"Unable to update PEX Account for BusinessAccountId: {session.PEXBusinessAcctId}");
                return Forbid();
            }

            return Ok();
        }

        [HttpGet, Route("ConnectionAccountDetail")]
        [ProducesResponseType(typeof(PexConnectionDetailModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PexConnectionDetailModel>> GetPexConnectionDetail(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();
            
            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);

            var connectionDetail = new PexConnectionDetailModel
            {
                Email = mapping.PEXEmailAccount,
                Name = mapping.PEXNameAccount,
                LastSync = mapping.LastSyncUtc
            };

            try
            {
                await _pexApiClient.GetToken(mapping.PEXExternalAPIToken);
                connectionDetail.Active = true;
            }
            catch (Exception)
            {
                connectionDetail.Active = false;
            }

            return Ok(connectionDetail);
        }
    }
}
