using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aplos.Api.Client.Models;
using System.Threading;

namespace Aplos.Api.Client.Abstractions
{
    public interface IAplosApiClientFactory
    {
        IAplosApiClient CreateClient(string aplosAccountId, string aplosClientId, string aplosPrivateKey, Uri aplosEndpointUri, Func<ILogger, AplosAuthModel> onAuthInitializing, Func<AplosAuthModel, ILogger, CancellationToken, Task> onTokenRefreshed);
    }
}
