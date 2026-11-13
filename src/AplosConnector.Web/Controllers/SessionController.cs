using Microsoft.AspNetCore.Mvc;
using AplosConnector.Common.Models;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using AplosConnector.Common.Models.Request;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Models.Response;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Models;
using AplosConnector.Common.Services.Abstractions;
using System.Threading;
using AplosConnector.Common.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public class SessionController : Controller
    {
        private readonly PexOAuthSessionStorage _pexOAuthSessionStorage;
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly AppSettingsModel _appSettings;
        private readonly IHttpClientFactory _clientFactory;
        private readonly IPexApiClient _pexApiClient;
        private readonly IAplosIntegrationService _aplosIntegrationService;
        private readonly ITimeLimitedDataProtector _callbackProtector;
        private readonly ITimeLimitedDataProtector _browserProtector;

        public const string PexLoginCookieName = "PexLogin";
        private static readonly TimeSpan PexLoginLifetime = TimeSpan.FromHours(1);

        public SessionController(
            PexOAuthSessionStorage pexOAuthSessionStorage,
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            IOptions<AppSettingsModel> appSettings,
            IHttpClientFactory clientFactory,
            IPexApiClient pexApiClient,
            IAplosIntegrationService aplosIntegrationService,
            IDataProtectionProvider dataProtectionProvider)
        {
            _pexOAuthSessionStorage = pexOAuthSessionStorage;
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _appSettings = appSettings.Value;
            _clientFactory = clientFactory;
            _pexApiClient = pexApiClient;
            _aplosIntegrationService = aplosIntegrationService;
            _callbackProtector = dataProtectionProvider?.CreateProtector("PexLogin.Callback").ToTimeLimitedDataProtector();
            _browserProtector = dataProtectionProvider?.CreateProtector("PexLogin.Browser").ToTimeLimitedDataProtector();
        }

        [HttpGet, Route("OAuthURL")]
        public async Task<string> PEXOAuthURL()
        {
            var httpClient = _clientFactory.CreateClient();
            httpClient.BaseAddress = _appSettings.PexConnectorBaseURL;

            var sessionId = Guid.NewGuid();
            var state = Uri.EscapeDataString(_callbackProtector.Protect(sessionId.ToString(), PexLoginLifetime));
            Response.Cookies.Append(PexLoginCookieName, _browserProtector.Protect(sessionId.ToString(), PexLoginLifetime), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = PexLoginLifetime,
            });

            var pexOAuthRequest = new OAuthRequestModel
            {
                AppId = _appSettings.PexApiClientId,
                AppSecret = _appSettings.PexApiClientSecret,
                ServerCallbackUrl = $"{_appSettings.AplosConnectorBaseURL}/api/Session/?sessionId={sessionId}&state={state}",
                BrowserClosingUrl = $"{_appSettings.AplosConnectorBaseURL}/finish-pex-login/{sessionId}"
            };
            var result = await httpClient.PostAsJsonAsync("Auth/Partner", pexOAuthRequest);

            var response = await result.Content.ReadAsStringAsync();
            return response;
        }

        [HttpPost, Route("FinishLogin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> FinishLogin(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            if (!IsProtectedSessionId(_browserProtector, Request.Cookies[PexLoginCookieName], sessionGuid))
            {
                DeletePexLoginCookie();
                return Unauthorized();
            }

            // Keep the cookie so the client can retry until the PEX callback has created the session.
            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return NotFound();

            DeletePexLoginCookie();
            return Ok();
        }

        [HttpPost, Route("AplosToken")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> CreateAplosToken(string sessionId, [FromBody] AplosTokenRequestModel model, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            PexOAuthSessionModel session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            Pex2AplosMappingModel mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(model.AplosClientId))
            {
                mapping.AplosClientId = model.AplosClientId;
            }

            if (!string.IsNullOrWhiteSpace(model.AplosPrivateKey))
            {
                mapping.AplosPrivateKey = model.AplosPrivateKey;
            }

            bool result = await _aplosIntegrationService.ValidateAplosApiCredentials(mapping, cancellationToken);
            if (!result) return BadRequest();

            await _pex2AplosMappingStorage.UpdateAsync(mapping, cancellationToken);

            return Ok();
        }

        [HttpPost, Route("JWT")]
        public async Task<TokenModel> CreateSessionFromJwt([FromBody] TokenModel model, CancellationToken cancellationToken)
        {
            var externalToken = await _pexApiClient.ExchangeJwtForApiToken(model.Token,
                new ExchangeTokenRequestModel { AppId = _appSettings.PexApiClientId, AppSecret = _appSettings.PexApiClientSecret });

            var business = await _pexApiClient.GetBusinessDetails(externalToken, cancellationToken);

            var sessionGuid = Guid.NewGuid();
            var session = new PexOAuthSessionModel
            {
                SessionGuid = sessionGuid,
                ExternalToken = externalToken,
                CreatedUtc = DateTime.UtcNow,
                LastRenewedUtc = DateTime.UtcNow,
                PEXBusinessAcctId = business.BusinessAccountId,
            };
            await _pexOAuthSessionStorage.CreateAsync(session, cancellationToken);

            await _aplosIntegrationService.EnsureMappingInstalled(session, cancellationToken);

            return new TokenModel { Token = sessionGuid.ToString() };
        }

        [HttpPost, Route("")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> CreateSession(string sessionId, string state, [FromBody] TokenModel model, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();
            if (!IsProtectedSessionId(_callbackProtector, state, sessionGuid)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(model?.Token)) return BadRequest();

            var business = await _pexApiClient.GetBusinessDetails(model.Token);
            var session = new PexOAuthSessionModel
            {
                SessionGuid = sessionGuid,
                ExternalToken = model.Token,
                CreatedUtc = DateTime.UtcNow,
                LastRenewedUtc = DateTime.UtcNow,
                PEXBusinessAcctId = business.BusinessAccountId
            };
            await _pexOAuthSessionStorage.CreateAsync(session, cancellationToken);

            await _aplosIntegrationService.EnsureMappingInstalled(session, cancellationToken);

            return Ok();
        }

        private void DeletePexLoginCookie() =>
            Response.Cookies.Delete(PexLoginCookieName, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Lax });

        private static bool IsProtectedSessionId(ITimeLimitedDataProtector protector, string protectedValue, Guid sessionGuid)
        {
            if (string.IsNullOrEmpty(protectedValue)) return false;
            try
            {
                return Guid.TryParse(protector.Unprotect(protectedValue), out var value) && value == sessionGuid;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
        }

        [HttpDelete, Route("")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> DeleteSession(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();
            var modelResult = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);

            if (modelResult != null)
            {
                var mappingResult = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(modelResult.PEXBusinessAcctId, cancellationToken);
                if (mappingResult == null)
                {
                    await _pexApiClient.DeleteExternalToken(modelResult.ExternalToken);
                }
                await _pexOAuthSessionStorage.DeleteBySessionGuidAsync(sessionGuid, cancellationToken);
            }

            return Ok();
        }

        [HttpGet("[action]")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<BusinessInfo>> BusinessInfo(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var modelResult = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (modelResult == null) return Unauthorized();

            var businessDetailsTask = _pexApiClient.GetBusinessDetails(modelResult.ExternalToken, cancellationToken);
            var businessSettingsTask = _pexApiClient.GetBusinessSettings(modelResult.ExternalToken, cancellationToken);

            await Task.WhenAll(businessDetailsTask, businessSettingsTask);

            return new BusinessInfo
            {
                BusinessName = businessDetailsTask.Result.BusinessName,
                FundingSource = businessSettingsTask.Result.FundingSource
            };
        }

        [HttpGet, Route("Validity")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<SessionValidityModel>> Validity(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var sessionValidityModel = new SessionValidityModel();

            try
            {
                var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
                sessionValidityModel.IsValid = session != null;
            }
            catch (Exception)
            {
                sessionValidityModel.IsValid = false;
            }

            return sessionValidityModel;
        }
    }
}
