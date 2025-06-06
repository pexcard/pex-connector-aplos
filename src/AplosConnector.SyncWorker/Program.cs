using System;
using System.Net.Http;
using Aplos.Api.Client;
using Aplos.Api.Client.Abstractions;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using AplosConnector.Common.VendorCards;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PexCard.Api.Client;
using PexCard.Api.Client.Core;
using PexCard.App.Infrastructure.AzureServiceBus.DependencyInjection;

#if !DEBUG
using PexCard.Shared.Encryption.AspNetCore;
using PexCard.Shared.Encryption.AspNetCore.Extensions;
#endif

namespace AplosConnector.SyncWorker;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = FunctionsApplication.CreateBuilder(args);

        builder.ConfigureFunctionsWebApplication();

        builder.Configuration
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
            .AddEnvironmentVariables();

#if !DEBUG
        if (!builder.Configuration.ProtectConfigurationSources(new ProtectConfigurationSourcesOptions { AssemblyTypeMarker = typeof(Program) }))
            return;
#endif

        builder.Services.AddLogging();
        
        builder.Services.Configure<AppSettingsModel>(builder.Configuration.GetSection("AppSettings"));
        
        var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettingsModel>()!;
        var storageConnectionString = builder.Configuration.GetConnectionString("StorageConnectionString")!;

        builder.Services.AddScoped(_ => new SyncSettingsModel());

        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient<IPexApiClient, PexApiClient>(client =>
        {
            client.BaseAddress = appSettings.PEXAPIBaseURL;

            var pexApiTimeoutSetting = appSettings.PEXAPITimeout;
            client.Timeout = TimeSpan.FromSeconds(pexApiTimeoutSetting);
        })
        .UsePexRetryPolicies<PexApiClient>();

        builder.Services.AddScoped<IStorageMappingService>(
            provider => new StorageMappingService(
                provider.GetService<IDataProtectionProvider>()
            ));

        var tableServiceClient = new TableServiceClient(storageConnectionString);
        builder.Services.TryAddSingleton(tableServiceClient);

        var pexOAuthSessionTableClient = tableServiceClient.GetTableClient(PexOAuthSessionStorage.TABLE_NAME);
        pexOAuthSessionTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(_ => new PexOAuthSessionStorage(pexOAuthSessionTableClient));

        var pex2AplosMappingTableClient = tableServiceClient.GetTableClient(Pex2AplosMappingStorage.TABLE_NAME);
        pex2AplosMappingTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(provider => new Pex2AplosMappingStorage(pex2AplosMappingTableClient,
            provider.GetService<IStorageMappingService>(), provider.GetService<ILogger<Pex2AplosMappingStorage>>()));

        var syncHistoryTableClient = tableServiceClient.GetTableClient(SyncHistoryStorage.TABLE_NAME);
        syncHistoryTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(_ => new SyncHistoryStorage(syncHistoryTableClient));

        var vendorCardTableClient = tableServiceClient.GetTableClient(VendorCardStorage.TABLE_NAME);
        vendorCardTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton<IVendorCardStorage>(provider => new VendorCardStorage(vendorCardTableClient,
            provider.GetService<IPexApiClient>(), provider.GetService<ILogger<VendorCardStorage>>()));

        builder.Services.AddAzureServiceBusSender(appSettings.AzureServiceBusUrl, appSettings.AzureServiceBusTopicName);

        var queueServiceClient = new QueueServiceClient(storageConnectionString);
        builder.Services.TryAddSingleton(queueServiceClient);

        var pex2AplosMappingQueueClient = queueServiceClient.GetQueueClient(Pex2AplosMappingQueue.QUEUE_NAME);
        pex2AplosMappingQueueClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(_ => new Pex2AplosMappingQueue(pex2AplosMappingQueueClient));

        builder.Services.AddScoped<IAccessTokenDecryptor>(provider => new AplosAccessTokenDecryptor());

        builder.Services.AddSingleton(provider => new SyncSettingsModel
        {
            SyncTransactionsIntervalDays = appSettings.SyncTransactionsIntervalDays
        });

        builder.Services.AddScoped<IAplosApiClientFactory>(provider => new AplosApiClientFactory(
            provider.GetService<IHttpClientFactory>(),
            provider.GetService<IAccessTokenDecryptor>(),
            provider.GetService<ILogger<AplosApiClientFactory>>()));

        builder.Services.AddScoped<IAplosIntegrationMappingService>(provider => new AplosIntegrationMappingService());
        builder.Services.AddScoped<IAplosIntegrationService>(provider => new AplosIntegrationService(
            provider.GetService<ILogger<AplosIntegrationService>>(),
            provider.GetService<IOptions<AppSettingsModel>>(),
            provider.GetService<IAplosApiClientFactory>(),
            provider.GetService<IAplosIntegrationMappingService>(),
            provider.GetService<IPexApiClient>(),
            provider.GetService<SyncHistoryStorage>(),
            provider.GetService<Pex2AplosMappingStorage>(),
            provider.GetService<SyncSettingsModel>(),
            provider.GetService<IVendorCardStorage>()));

        var storageAccount = CloudStorageAccount.Parse(storageConnectionString);
        var blobClient = storageAccount.CreateCloudBlobClient();
        var blobContainer = blobClient.GetContainerReference(appSettings.DataProtectionBlobContainer);
        blobContainer.CreateIfNotExistsAsync();

        builder.Services
            .AddDataProtection()
            .SetApplicationName(appSettings.DataProtectionApplicationName)
            .PersistKeysToAzureBlobStorage(
                blobContainer,
                appSettings.DataProtectionBlobName)
            .ProtectKeysWithAzureKeyVault(
                new Uri(appSettings.DataProtectionKeyIdentifier),
                new DefaultAzureCredential())
            .DisableAutomaticKeyGeneration();

        var host = builder.Build();

        host.Run();
    }
}
