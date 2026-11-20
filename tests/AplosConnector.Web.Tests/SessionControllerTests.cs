using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Request;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using AplosConnector.Web.Controllers;
using Azure.Data.Tables;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Moq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AplosConnector.Web.Tests
{
    public class SessionControllerTests
    {
        private static readonly Guid SessionGuid = new("0b7c2f4e-5a1d-4e8b-9f3a-2c6d8e1f4a7b");
        private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();
        private readonly Mock<PexOAuthSessionStorage> _sessions = new(MockBehavior.Loose, (TableClient)null);

        private readonly Mock<IPexApiClient> _pexApiClient = new();
        private readonly Mock<IAplosIntegrationService> _aplosIntegrationService = new();
        private readonly AppSettingsModel _appSettings = new()
        {
            PexConnectorBaseURL = new Uri("https://pex.test/"),
            AplosConnectorBaseURL = "https://aplos.test",
        };

        private SessionController BuildController(string pexLoginCookie = null, IHttpClientFactory httpClientFactory = null)
        {
            var httpContext = new DefaultHttpContext();
            if (pexLoginCookie != null)
            {
                httpContext.Request.Headers.Cookie = $"{SessionController.PexLoginCookieName}={pexLoginCookie}";
            }

            return new SessionController(_sessions.Object, null, Options.Create(_appSettings), httpClientFactory, _pexApiClient.Object, _aplosIntegrationService.Object, _dataProtection)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        private string Protect(string purpose, Guid sessionGuid) =>
            _dataProtection.CreateProtector(purpose).ToTimeLimitedDataProtector().Protect(sessionGuid.ToString(), TimeSpan.FromHours(1));

        private void SetupSession() =>
            _sessions.Setup(s => s.GetBySessionGuidAsync(SessionGuid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PexOAuthSessionModel { SessionGuid = SessionGuid });

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public string Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"OAuthUrl\":\"https://pex.test/auth\"}") };
            }
        }

        [Fact]
        public async Task PEXOAuthURL_IssuesStateAndCookieAcceptedByCallbackAndFinishLogin()
        {
            var handler = new RecordingHandler();
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
            var oAuthController = BuildController(httpClientFactory: httpClientFactory.Object);

            await oAuthController.PEXOAuthURL();

            var request = JsonSerializer.Deserialize<OAuthRequestModel>(handler.Body);
            var callbackUrl = new Uri(request.ServerCallbackUrl);
            Assert.Equal("https://aplos.test/api/Session/", callbackUrl.GetLeftPart(UriPartial.Path));
            var query = QueryHelpers.ParseQuery(callbackUrl.Query);
            var sessionId = query["sessionId"].ToString();
            Assert.Equal($"https://aplos.test/finish-pex-login/{sessionId}", request.BrowserClosingUrl);

            var cookie = SetCookieHeaderValue.ParseList(oAuthController.Response.Headers.SetCookie.ToArray())
                .Single(c => c.Name == SessionController.PexLoginCookieName);
            Assert.True(cookie.HttpOnly);
            Assert.True(cookie.Secure);
            Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);

            _pexApiClient.Setup(c => c.GetBusinessDetails("token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessDetailsModel { BusinessAccountId = 42 });

            var createResult = await BuildController().CreateSession(sessionId, query["state"], new TokenModel { Token = "token" }, default);

            Assert.IsType<OkResult>(createResult);
            _sessions.Verify(s => s.CreateAsync(
                It.Is<PexOAuthSessionModel>(m => m.SessionGuid == Guid.Parse(sessionId) && m.ExternalToken == "token" && m.PEXBusinessAcctId == 42),
                It.IsAny<CancellationToken>()), Times.Once);

            _sessions.Setup(s => s.GetBySessionGuidAsync(Guid.Parse(sessionId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PexOAuthSessionModel { SessionGuid = Guid.Parse(sessionId) });

            var finishResult = await BuildController(cookie.Value.ToString()).FinishLogin(sessionId, default);

            Assert.IsType<OkResult>(finishResult);
        }

        [Fact]
        public async Task FinishLogin_ReturnsNotFoundAndKeepsCookie_WhenSessionNotCreatedYet()
        {
            var controller = BuildController(Protect("PexLogin.Browser", SessionGuid));

            var result = await controller.FinishLogin(SessionGuid.ToString(), default);

            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
        }

        [Fact]
        public async Task FinishLogin_ReturnsOk_WhenCookieMatchesSession()
        {
            SetupSession();
            var controller = BuildController(Protect("PexLogin.Browser", SessionGuid));

            var result = await controller.FinishLogin(SessionGuid.ToString(), default);

            Assert.IsType<OkResult>(result);
            Assert.Contains(SessionController.PexLoginCookieName, controller.Response.Headers.SetCookie.ToString());
        }

        [Fact]
        public async Task FinishLogin_ReturnsUnauthorized_WithoutCookie()
        {
            SetupSession();

            var result = await BuildController().FinishLogin(SessionGuid.ToString(), default);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task FinishLogin_ReturnsUnauthorized_WhenCookieIsForAnotherSession()
        {
            SetupSession();

            var result = await BuildController(Protect("PexLogin.Browser", Guid.NewGuid())).FinishLogin(SessionGuid.ToString(), default);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task FinishLogin_ReturnsUnauthorized_WithCallbackState()
        {
            SetupSession();

            var result = await BuildController(Protect("PexLogin.Callback", SessionGuid)).FinishLogin(SessionGuid.ToString(), default);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task CreateSession_ReturnsUnauthorized_WithoutState()
        {
            var result = await BuildController().CreateSession(SessionGuid.ToString(), null, new TokenModel { Token = "token" }, default);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task CreateSession_ReturnsUnauthorized_WhenStateIsForAnotherSession()
        {
            var state = Protect("PexLogin.Callback", Guid.NewGuid());

            var result = await BuildController().CreateSession(SessionGuid.ToString(), state, new TokenModel { Token = "token" }, default);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task CreateSession_ReturnsUnauthorized_WithBrowserCookieValue()
        {
            var state = Protect("PexLogin.Browser", SessionGuid);

            var result = await BuildController().CreateSession(SessionGuid.ToString(), state, new TokenModel { Token = "token" }, default);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public void CreateAplosToken_ReturnsOk_WithValidSessionIdAndNonExistentSettings()
        {
        }

        [Fact]
        public void CreateAplosToken_ReturnsOk_WithValidSessionIdAndExistingtSettings()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public async Task CreateAplosToken_ReturnsBadRequest_WithInvalidSessionId(string sessionId)
        {
            //Arrange
            var controller = new SessionController(null, null, Options.Create<AppSettingsModel>(null), null, null, null, null);

            //Act
            var result = await controller.CreateAplosToken(sessionId, null, default);

            //Assert
            Assert.Equal(typeof(BadRequestResult), result.GetType());
        }

        [Fact]
        public void CreateAplosToken_ReturnsUnauthorized_WithNonExistentSessionId()
        {
        }

        [Fact]
        public void CreateAplosToken_ReturnsForbidResult_WithValidSessionIdAndInvalidAplosCredentials()
        {
        }

        [Fact]
        public void Validity_ReturnsOkResult_WithValidSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public void Validity_ReturnsBadRequestResult_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public void PexBusinessName_ReturnsOkResult_WithValidSessionId()
        {
        }

        [Fact]
        public void PexBusinessName_ReturnsUnauthorizedResult_WithNonExistentSessionId()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public void PexBusinessName_ReturnsBadRequestResult_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public void DeleteSession_ReturnsOkResult_WithValidSessionIdAndExistingSession()
        {
        }

        [Fact]
        public void DeleteSession_ReturnsOkResult_WithValidSessionIdAndNonExistentSession()
        {
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        [InlineData("42")]
        [InlineData("not a guid")]
        public void DeleteSession_ReturnsBadRequestResult_WithInvalidSessionId(string sessionId)
        {
        }

        [Fact]
        public void CreateSession_ReturnsOkResult_WithValidInput()
        {
        }
    }
}
