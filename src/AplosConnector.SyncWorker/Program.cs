using System;
using System.Linq;
using System.Net.Http;
using Aplos.Api.Client;
using Aplos.Api.Client.Abstractions;
using AplosConnector.Common.Correlation;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using AplosConnector.Common.Tracing;
using AplosConnector.Common.VendorCards;
using AplosConnector.SyncWorker.Correlation;
using AplosConnector.SyncWorker.Tracing;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PexCard.Api.Client;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Interfaces;
using PexCard.App.Infrastructure.AzureServiceBus.DependencyInjection;
using PexCard.Shared.OpenTelemetry.Abstractions.Options;
using PexCard.Shared.OpenTelemetry.Extensions;

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

        // Links queue invocations to the producer's trace; no-op for HTTP and timer triggers.
        builder.UseMiddleware<QueueTraceLinkMiddleware>();
        builder.UseMiddleware<CorrelationIdMiddleware>();

        builder.Configuration
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
            .AddEnvironmentVariables();

#if !DEBUG
        if (!builder.Configuration.ProtectConfigurationSources(new ProtectConfigurationSourcesOptions { AssemblyTypeMarker = typeof(Program) }))
            return;
#endif

        // KeyVault:Uri comes from the app's KeyVault__Uri setting, never git; secrets use "--" for ":" (TelemetryConfig--Exporter--BaseUrl).
        var kvUri = builder.Configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(kvUri))
        {
            builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
        }

        // Before ConfigurePexTelemetry, so its service.name wins over the Functions detector's.
        builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

        var telemetryOptions = builder.Configuration.GetSection("TelemetryConfig").Get<PexTelemetryOptions>()
            ?? throw new InvalidOperationException("Telemetry configuration is missing.");
        telemetryOptions = telemetryOptions with
        {
            Resource = telemetryOptions.Resource with
            {
                ServiceName = "pex-connector-aplos-func",
                DeploymentHostType = "azure-functions"
            },
            // Scopes carry TraceId/SpanId, faas.* and the correlation id baggage onto exported logs.
            Logging = telemetryOptions.Logging with
            {
                IncludeScopes = true
            }
        };
        builder.Services.ConfigurePexTelemetry(telemetryOptions);

        // After ConfigurePexTelemetry: the host hands queue and timer invocations an unsampled parent, which ParentBased would drop.
        builder.Services.AddOpenTelemetry().WithTracing(b => b
            .SetSampler(new AlwaysOnSampler())
            .AddSource(SyncTelemetry.ActivitySourceName));

        // Session deletes hit the session table by row key, which is the session id; keep it out of outbound spans.
        builder.Services.AddSessionIdRedaction();

        // Drop the Functions detector's deployment.environment.name (PEX uses deployment.environment); runs last.
        builder.Services.AddOpenTelemetry().ConfigureResource(r =>
        {
            var kept = r.Build().Attributes
                .Where(attr => attr.Key != "deployment.environment.name")
                .ToList();
            r.Clear();
            r.AddAttributes(kept);
        });

        // Baggage surfaces pex.correlation_id on log records; the default trio must stay.
        builder.Logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId |
            ActivityTrackingOptions.ParentId | ActivityTrackingOptions.Baggage);

        // PexApiClient picks up the resolver and sends X-Correlation-Id on every call.
        builder.Services.AddSingleton(new CorrelationIdAccessor(CorrelationConstants.Prefix));
        builder.Services.AddSingleton<ICorrelationIdResolver, CorrelationIdResolver>();

        builder.Services.AddLogging();
        
        builder.Services.Configure<AppSettingsModel>(builder.Configuration.GetSection("AppSettings"));
        
        var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettingsModel>()!;
        var storageAccountName = appSettings.StorageAccountName
            ?? throw new InvalidOperationException("AppSettings:StorageAccountName is not configured.");
        var credential = new DefaultAzureCredential();

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

        var tableServiceClient = new TableServiceClient(
            new Uri($"https://{storageAccountName}.table.core.windows.net"),
            credential);
        builder.Services.TryAddSingleton(tableServiceClient);

        var pexOAuthSessionTableClient = tableServiceClient.GetTableClient(PexOAuthSessionStorage.TABLE_NAME);
        pexOAuthSessionTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(_ => new PexOAuthSessionStorage(pexOAuthSessionTableClient));

        var pex2AplosMappingTableClient = tableServiceClient.GetTableClient(Pex2AplosMappingStorage.TABLE_NAME);
        pex2AplosMappingTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(provider => new Pex2AplosMappingStorage(pex2AplosMappingTableClient,
            provider.GetService<IStorageMappingService>(), provider.GetService<ILogger<Pex2AplosMappingStorage>>()));

        var aplosBillMappingTableClient = tableServiceClient.GetTableClient(AplosBillMappingStorage.TABLE_NAME);
        aplosBillMappingTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton<IAplosBillMappingStorage>(_ => new AplosBillMappingStorage(aplosBillMappingTableClient));

        var aplosVendorCardOrderTableClient = tableServiceClient.GetTableClient(AplosVendorCardOrderStorage.TABLE_NAME);
        aplosVendorCardOrderTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton<IAplosVendorCardOrderStorage>(_ => new AplosVendorCardOrderStorage(aplosVendorCardOrderTableClient));

        var syncHistoryTableClient = tableServiceClient.GetTableClient(SyncHistoryStorage.TABLE_NAME);
        syncHistoryTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton(_ => new SyncHistoryStorage(syncHistoryTableClient));

        var vendorCardTableClient = tableServiceClient.GetTableClient(VendorCardStorage.TABLE_NAME);
        vendorCardTableClient.CreateIfNotExistsAsync();
        builder.Services.AddSingleton<IVendorCardStorage>(provider => new VendorCardStorage(vendorCardTableClient,
            provider.GetService<IPexApiClient>(), provider.GetService<ILogger<VendorCardStorage>>()));

        builder.Services.AddAzureServiceBusSender(appSettings.AzureServiceBusUrl, appSettings.AzureServiceBusTopicName);

        var queueServiceClient = new QueueServiceClient(
            new Uri($"https://{storageAccountName}.queue.core.windows.net"),
            credential);
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
            provider.GetService<IVendorCardStorage>(),
            provider.GetService<IAplosBillMappingStorage>(),
            provider.GetService<IAplosVendorCardOrderStorage>()));

        var blobServiceClient = new BlobServiceClient(
            new Uri($"https://{storageAccountName}.blob.core.windows.net"),
            credential);
        var blobContainerClient = blobServiceClient.GetBlobContainerClient(appSettings.DataProtectionBlobContainer);
        blobContainerClient.CreateIfNotExists();
        var blobClient = blobContainerClient.GetBlobClient(appSettings.DataProtectionBlobName);

        builder.Services
            .AddDataProtection()
            .SetApplicationName(appSettings.DataProtectionApplicationName)
            .PersistKeysToAzureBlobStorage(blobClient)
            .ProtectKeysWithAzureKeyVault(
                new Uri(appSettings.DataProtectionKeyIdentifier),
                credential)
            .DisableAutomaticKeyGeneration();

        var host = builder.Build();

        host.Run();
    }
}
