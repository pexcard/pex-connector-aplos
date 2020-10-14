using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aplos.Api.Client.Models;

namespace Aplos.Api.Client.Abstractions
{
    public interface IAplosApiClientFactory
    {
        IAplosApiClient CreateClient(string aplosClientId, string aplosPrivateKey, Uri aplosEndpointUri, Func<ILogger, AplosAuthModel> onAuthInitializing, Func<AplosAuthModel, ILogger, Task> onTokenRefreshed);
    }
}
