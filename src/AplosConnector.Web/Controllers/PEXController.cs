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
using LazyCache;
using AplosConnector.Common.VendorCards;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public class PexController : Controller
    {
        private readonly IPexApiClient _pexApiClient;
        private readonly PexOAuthSessionStorage _pexOAuthSessionStorage;
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly IVendorCardRepository _vendorCardRepository;
        private readonly IVendorCardService _vendorCardService;
        private readonly ILogger<PexController> _logger;
        private readonly IAppCache _cache;

        public PexController(
            IPexApiClient pexApiClient,
            IVendorCardRepository vendorCardRepository,
            IVendorCardService vendorCardService,
            PexOAuthSessionStorage pexOAuthSessionStorage,
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            ILogger<PexController> logger,
            IAppCache cache)
        {
            _pexApiClient = pexApiClient;
            _vendorCardRepository = vendorCardRepository;
            _vendorCardService = vendorCardService;
            _pexOAuthSessionStorage = pexOAuthSessionStorage;
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _logger = logger;
            _cache = cache;
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
                LastSync = mapping.LastSyncUtc,
                SyncingSetup = mapping.SyncInvoices || mapping.SyncTransactions || mapping.SyncTransfers || mapping.SyncPexFees || mapping.SyncTags || mapping.SyncFundsToPex || mapping.SyncTaxTagToPex
            };

            try
            {
                if (!string.IsNullOrEmpty(mapping.PEXExternalAPIToken))
                {
                    var tokenTask = _pexApiClient.GetToken(mapping.PEXExternalAPIToken, cancellationToken);
                    var businessSettingsTask = _cache.GetOrAddAsync($"PexBusinessSettings:{session.PEXBusinessAcctId}",
                        () => _pexApiClient.GetBusinessSettings(mapping.PEXExternalAPIToken, cancellationToken), TimeSpan.FromMinutes(5));
                    var businessDetailsTask = _cache.GetOrAddAsync($"PexBusinessDetails:{session.PEXBusinessAcctId}",
                        () => _pexApiClient.GetBusinessDetails(mapping.PEXExternalAPIToken, cancellationToken), TimeSpan.FromMinutes(5));

                    await Task.WhenAll(tokenTask, businessSettingsTask, businessDetailsTask);

                    connectionDetail.PexConnection = true;

                    var businessSettings = businessSettingsTask.Result;
                    var businessDetails = businessDetailsTask.Result;

                    connectionDetail.AccountBalance = businessDetails.BusinessAccountBalance;
                    connectionDetail.VendorCardsAvailable = businessSettings.VendorLimit - businessDetails.OpenVendorCardsCount;
                    connectionDetail.IsPrepaid = businessSettings.FundingSource == FundingSource.Prepaid;
                    connectionDetail.IsCredit = businessSettings.FundingSource == FundingSource.Credit;
                }
                else
                {
                    connectionDetail.PexConnection = false;
                }
            }
            catch (Exception)
            {
                connectionDetail.PexConnection = false;
            }

            try
            {
                if (string.IsNullOrEmpty(mapping.AplosAccessToken))
                {
                    connectionDetail.AplosConnection = false;
                }
                else
                {
                    connectionDetail.AplosConnection = true;

                    try
                    {
                        var aplosVendorCardOrders = await _vendorCardRepository.GetAllVendorCardsOrderedAsync(mapping, cancellationToken);
                        connectionDetail.VendorsSetup = aplosVendorCardOrders?.Count > 0;
                    }
                    catch (Exception)
                    {
                        connectionDetail.VendorsSetup = false;
                    }
                }
            }
            catch (Exception)
            {
                connectionDetail.AplosConnection = false;
            }

            return Ok(connectionDetail);
        }

        [HttpPost, Route("VendorCards")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateVendorCards(string sessionId, [FromBody] List<VendorCardOrder> orders, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var vendorCardsOrdered = await _vendorCardService.OrderVendorCardsAsync(mapping, orders, CancellationToken.None);

            await _vendorCardRepository.SaveVendorCardsOrderedAsync(mapping, vendorCardsOrdered, CancellationToken.None);

            return Ok();
        }

        [HttpGet, Route("VendorCards")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetVendorCards(string sessionId, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var userProfile = await _pexApiClient.GetMyAdminProfile(session.ExternalToken);

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            mapping ??= new Pex2AplosMappingModel();

            mapping.PEXExternalAPIToken = session.ExternalToken;
            mapping.PEXEmailAccount = userProfile?.Admin?.Email;
            mapping.PEXNameAccount = $"{userProfile?.Admin?.FirstName} {userProfile?.Admin?.LastName}";

            var order = await _vendorCardRepository.GetAllVendorCardsOrderedAsync(mapping, cancellationToken);

            return Ok(order);
        }
    }
}
