using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models;

namespace Aplos.Api.Client
{
    public class AplosApiClientFactory : IAplosApiClientFactory
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly IAccessTokenDecryptor _accessTokenDecryptor;
        private readonly ILogger _logger;

        public AplosApiClientFactory(
            IHttpClientFactory clientFactory,
            IAccessTokenDecryptor accessTokenDecryptor,
            ILogger logger)
        {
            _clientFactory = clientFactory;
            _accessTokenDecryptor = accessTokenDecryptor;
            _logger = logger;
        }

        public IAplosApiClient CreateClient(
            string aplosClientId,
            string aplosPrivateKey,
            Uri aplosEndpointUri,
            Func<ILogger, AplosAuthModel> onAuthInitializing,
            Func<AplosAuthModel, ILogger, Task> onTokenRefreshed)
        {
            return new AplosApiClient(
                aplosClientId,
                aplosPrivateKey,
                aplosEndpointUri,
                _clientFactory,
                _accessTokenDecryptor,
                _logger,
                onAuthInitializing,
                onTokenRefreshed);
        }
    }
}
