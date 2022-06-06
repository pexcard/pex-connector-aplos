using System;
using System.Net.Http;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aplos.Api.Client;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Core.Storages;
using AplosConnector.SyncWorker;
using AplosConnector.SyncWorker.Models;
using PexCard.Api.Client;
using PexCard.Api.Client.Core;
using Polly;
using Polly.Extensions.Http;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Services;
using Aplos.Api.Client.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

[assembly: FunctionsStartup(typeof(Startup))]
namespace AplosConnector.SyncWorker
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddLogging();

            builder.Services.AddOptions<AppSettingsModel>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.Bind(settings);
                });

            builder.Services.AddHttpClient();
            builder.Services.AddHttpClient<IPexApiClient, PexApiClient>((client) =>
            {
                client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("PEXAPIBaseURL", EnvironmentVariableTarget.Process));

                var pexApiTimeoutSetting = Environment.GetEnvironmentVariable("PEXAPITimeout", EnvironmentVariableTarget.Process);
                if (!int.TryParse(pexApiTimeoutSetting, out var pexApiTimeout))
                {
                    pexApiTimeout = 100;
                }
                client.Timeout = TimeSpan.FromSeconds(pexApiTimeout);
            })
            .AddPolicyHandler(GetPexRetryPolicy());

            builder.Services.AddScoped<IStorageMappingService>(
                provider => new StorageMappingService(
                    provider.GetService<IDataProtectionProvider>()
                ));

            string storageConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString", EnvironmentVariableTarget.Process);

            builder.Services.AddSingleton(provider => 
                new Pex2AplosMappingStorage(
                    Environment.GetEnvironmentVariable("StorageConnectionString", EnvironmentVariableTarget.Process),
                    provider.GetService<IStorageMappingService>(),
                    provider.GetService<ILogger<Pex2AplosMappingStorage>>())
                .InitTable());
            builder.Services.AddSingleton(provider => new PexOAuthSessionStorage(Environment.GetEnvironmentVariable("StorageConnectionString", EnvironmentVariableTarget.Process)).InitTable());
            builder.Services.AddSingleton(provider => new Pex2AplosMappingQueue(Environment.GetEnvironmentVariable("StorageConnectionString", EnvironmentVariableTarget.Process)).InitQueue());
            builder.Services.AddSingleton(provider => new SyncResultStorage(Environment.GetEnvironmentVariable("StorageConnectionString", EnvironmentVariableTarget.Process)).InitTable());

            builder.Services.AddScoped<IAccessTokenDecryptor>(provider => new AplosAccessTokenDecryptor());

            builder.Services.AddSingleton(provider =>
            {
                var syncTransactionsInterval =
                    Environment.GetEnvironmentVariable("SyncTransactionsIntervalDays", EnvironmentVariableTarget.Process);
                if (!int.TryParse(syncTransactionsInterval, out var syncTransactionsIntervalDays))
                {
                    syncTransactionsIntervalDays = 60;
                }

                var result = new SyncSettingsModel
                {
                    SyncTransactionsIntervalDays = syncTransactionsIntervalDays
                };
                return result;
            });

            builder.Services.AddScoped<IAplosApiClientFactory>(provider => new AplosApiClientFactory(
                provider.GetService<IHttpClientFactory>(),
                provider.GetService<IAccessTokenDecryptor>(),
                provider.GetService<ILogger<AplosApiClientFactory>>()));

            builder.Services.AddScoped<IAplosIntegrationMappingService>(provider => new AplosIntegrationMappingService());
            builder.Services.AddScoped<IAplosIntegrationService>(provider => new AplosIntegrationService(
                provider.GetService<IOptions<AppSettingsModel>>(),
                provider.GetService<IAplosApiClientFactory>(),
                provider.GetService<IAplosIntegrationMappingService>(),
                provider.GetService<IPexApiClient>(),
                provider.GetService<SyncResultStorage>(),
                provider.GetService<Pex2AplosMappingStorage>()));

            var dataProtectionApplicationName = Environment.GetEnvironmentVariable("DataProtectionApplicationName", EnvironmentVariableTarget.Process);
            var dataProtectionBlobContainer = Environment.GetEnvironmentVariable("DataProtectionBlobContainer", EnvironmentVariableTarget.Process);
            var dataProtectionBlobName = Environment.GetEnvironmentVariable("DataProtectionBlobName", EnvironmentVariableTarget.Process);
            var dataProtectionKeyIdentifier = Environment.GetEnvironmentVariable("DataProtectionKeyIdentifier", EnvironmentVariableTarget.Process);

            var storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer blobContainer = blobClient.GetContainerReference(dataProtectionBlobContainer);
            blobContainer.CreateIfNotExistsAsync();

            var keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(new AzureServiceTokenProvider().KeyVaultTokenCallback));

            builder.Services
                .AddDataProtection()
                .SetApplicationName(dataProtectionApplicationName)
                .PersistKeysToAzureBlobStorage(
                    blobContainer,
                    dataProtectionBlobName)
                .ProtectKeysWithAzureKeyVault(
                    keyVaultClient,
                    dataProtectionKeyIdentifier)
                .DisableAutomaticKeyGeneration();
        }

        private static IAsyncPolicy<HttpResponseMessage> GetPexRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .RetryAsync(3);
        }
    }
}