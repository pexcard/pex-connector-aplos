using Aplos.Api.Client;
using Aplos.Api.Client.Abstractions;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using AplosConnector.Web.Spa;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using PexCard.Api.Client;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using AplosConnector.Common.Storage;
using AplosConnector.Common.VendorCards;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Azure.Storage.Queues;
using Azure.Identity;
using AplosConnector.Common.Correlation;
using AplosConnector.Common.Tracing;
using AplosConnector.Web.Telemetry;
using OpenTelemetry.Resources;
using PexCard.Api.Client.Core.Interfaces;
using PexCard.Shared.OpenTelemetry.Abstractions.Constants;
using PexCard.Shared.OpenTelemetry.AspNetCore;
using PexCard.Shared.OpenTelemetry.Extensions;

namespace AplosConnector.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private readonly IConfiguration _configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // PexApiClient sends the current correlation id as X-Correlation-Id on every outbound call.
            services.AddSingleton(new CorrelationIdAccessor(CorrelationConstants.Prefix));
            services.AddSingleton<ICorrelationIdResolver, CorrelationIdResolver>();

            // Before ConfigurePexTelemetry so its explicit service.name wins over the detector's WEBSITE_SITE_NAME fallback.
            services.AddOpenTelemetry().ConfigureResource(b => b.AddAzureAppServiceDetector());

            services.ConfigurePexTelemetry(_configuration);

            // Manual syncs run in this process, so listen for the sync spans here too.
            services.AddOpenTelemetry().WithTracing(b => b.AddSource(SyncTelemetry.ActivitySourceName));

            services
                .AddControllers()
                .AddNewtonsoftJson();

            // One summary record per request, honoring [NoLog]; UseHttpLogging runs after UseRouting.
            services.AddPexHttpLogging();
            services.AddSessionPathRedaction();

            services.AddOptions();

            var configSection = _configuration.GetSection("AppSettings");
            var appSettings = configSection.Get<AppSettingsModel>();
            services.Configure<AppSettingsModel>(configSection);

            services.AddScoped(_ => new SyncSettingsModel());

            services.AddHttpClient();
            services.AddHttpClient<IPexApiClient, PexApiClient>((client) =>
            {
                client.BaseAddress = appSettings.PEXAPIBaseURL;
                client.Timeout = TimeSpan.FromSeconds(appSettings.PEXAPITimeout);
            })
            .UsePexRetryPolicies<PexApiClient>();

            services.AddLazyCache();

            services.AddSingleton<IStorageMappingService>(
                provider => new StorageMappingService(
                    provider.GetService<IDataProtectionProvider>()
                ));

            var storageAccountName = appSettings.StorageAccountName
                ?? throw new InvalidOperationException("AppSettings:StorageAccountName is not configured.");
            var credential = new DefaultAzureCredential();

            var tableServiceClient = new TableServiceClient(
                new Uri($"https://{storageAccountName}.table.core.windows.net"),
                credential);
            services.TryAddSingleton(tableServiceClient);

            var pexOAuthSessionTableClient = tableServiceClient.GetTableClient(PexOAuthSessionStorage.TABLE_NAME);
            pexOAuthSessionTableClient.CreateIfNotExistsAsync();
            services.AddSingleton(_ => new PexOAuthSessionStorage(pexOAuthSessionTableClient));

            var pex2AplosMappingTableClient = tableServiceClient.GetTableClient(Pex2AplosMappingStorage.TABLE_NAME);
            pex2AplosMappingTableClient.CreateIfNotExistsAsync();
            services.AddSingleton(provider => new Pex2AplosMappingStorage(pex2AplosMappingTableClient, 
                provider.GetService<IStorageMappingService>(), provider.GetService<ILogger<Pex2AplosMappingStorage>>()));

            var aplosBillMappingTableClient = tableServiceClient.GetTableClient(AplosBillMappingStorage.TABLE_NAME);
            aplosBillMappingTableClient.CreateIfNotExistsAsync();
            services.AddSingleton<IAplosBillMappingStorage>(_ => new AplosBillMappingStorage(aplosBillMappingTableClient));

            var aplosVendorCardOrderTableClient = tableServiceClient.GetTableClient(AplosVendorCardOrderStorage.TABLE_NAME);
            aplosVendorCardOrderTableClient.CreateIfNotExistsAsync();
            services.AddSingleton<IAplosVendorCardOrderStorage>(_ => new AplosVendorCardOrderStorage(aplosVendorCardOrderTableClient));

            var syncHistoryTableClient = tableServiceClient.GetTableClient(SyncHistoryStorage.TABLE_NAME);
            syncHistoryTableClient.CreateIfNotExistsAsync();
            services.AddSingleton(_ => new SyncHistoryStorage(syncHistoryTableClient));

            var vendorCardTableClient = tableServiceClient.GetTableClient(VendorCardStorage.TABLE_NAME);
            vendorCardTableClient.CreateIfNotExistsAsync();
            services.AddSingleton<IVendorCardStorage>(provider => new VendorCardStorage(vendorCardTableClient,
                provider.GetService<IPexApiClient>(), provider.GetService<ILogger<VendorCardStorage>>()));

            var queueServiceClient = new QueueServiceClient(
                new Uri($"https://{storageAccountName}.queue.core.windows.net"),
                credential);
            services.TryAddSingleton(queueServiceClient);

            var pex2AplosMappingQueueClient = queueServiceClient.GetQueueClient(Pex2AplosMappingQueue.QUEUE_NAME);
            pex2AplosMappingQueueClient.CreateIfNotExistsAsync();
            services.AddSingleton(_ => new Pex2AplosMappingQueue(pex2AplosMappingQueueClient));

            services.AddScoped<IVendorCardService, VendorCardService>();
            
            services.AddScoped<IAccessTokenDecryptor>(_ => new AplosAccessTokenDecryptor());

            services.AddScoped<IAplosApiClientFactory>(provider => new AplosApiClientFactory(
                provider.GetService<IHttpClientFactory>(),
                provider.GetService<IAccessTokenDecryptor>(),
                provider.GetService<ILogger<AplosApiClientFactory>>()));

            services.AddScoped<IAplosIntegrationMappingService>(_ => new AplosIntegrationMappingService());
            services.AddScoped<IAplosIntegrationService>(provider => new AplosIntegrationService(
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

            services.AddCors(options =>
            {
                options.AddPolicy("AllowedOrigins",
                    builder =>
                    {
                        if (string.IsNullOrEmpty(appSettings.CorsAllowedOrigins))
                        {
                            builder.AllowAnyOrigin();
                        }
                        else
                        {
                            builder.WithOrigins(appSettings.CorsAllowedOrigins.Split(";"));
                        }

                        builder.AllowAnyHeader()
                        .AllowAnyMethod();
                    });
            });

            var blobServiceClient = new BlobServiceClient(
                new Uri($"https://{storageAccountName}.blob.core.windows.net"),
                credential);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(appSettings.DataProtectionBlobContainer);
            blobContainerClient.CreateIfNotExists();
            var blobClient = blobContainerClient.GetBlobClient(appSettings.DataProtectionBlobName);

            services
                .AddDataProtection()
                .SetApplicationName(appSettings.DataProtectionApplicationName)
                .PersistKeysToAzureBlobStorage(blobClient)
                .ProtectKeysWithAzureKeyVault(
                    new Uri(appSettings.DataProtectionKeyIdentifier),
                    credential);


            // In production, the Angular files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "ClientApp/dist";
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // First, so everything downstream sees the id: response header, outbound PEX calls, request span tag and log scope.
            var correlationAccessor = app.ApplicationServices.GetRequiredService<CorrelationIdAccessor>();
            app.Use(async (ctx, next) =>
            {
                var inbound = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault();
                var id = IsValidInboundCorrelationId(inbound)
                    ? inbound
                    : correlationAccessor.Generate();
                ctx.Response.Headers["X-Correlation-Id"] = id;

                var prior = correlationAccessor.Current;
                correlationAccessor.Current = id;
                Activity.Current?.SetTag(DiagnosticNames.CorrelationId, id);

                var logger = ctx.RequestServices.GetRequiredService<ILogger<Startup>>();
                try
                {
                    using (logger.BeginScope(new Dictionary<string, object> { [DiagnosticNames.CorrelationId] = id }))
                    {
                        await next();
                    }
                }
                finally
                {
                    correlationAccessor.Current = prior;
                }
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler(a => a.Run(async context =>
                {
                    var feature = context.Features.Get<IExceptionHandlerPathFeature>();
                    var exception = feature.Error;
                    if (exception != null)
                    {
                        string responseContent;
                        if (exception is PexApiClientException pexException)
                        {
                            context.Response.StatusCode = (int)pexException.Code;
                            responseContent = pexException.Message;
                        }
                        else
                        {
                            HttpStatusCode statusCode;
                            if (exception.Source.Equals("Microsoft.AspNetCore.SpaServices.Extensions") &&
                                exception.Message.Contains("no other middleware handled the request",
                                    StringComparison.InvariantCultureIgnoreCase))
                            {
                                statusCode = HttpStatusCode.NotFound;
                            }
                            else
                            {
                                statusCode = HttpStatusCode.InternalServerError;
                            }

                            context.Response.StatusCode = (int)statusCode;
                            var error = new { Error = exception.Message };
                            responseContent = JsonConvert.SerializeObject(error);
                        }

                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(responseContent);
                    }
                }));

                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();


            app.Use(async (ctx, next) =>
            {
                ctx.Response.Headers.Add("Content-Security-Policy", "script-src 'self' 'unsafe-eval'; http://localhost:44300 https://*.ngrok.io https://*.pexcard.com; frame-ancestors 'none';");
                ctx.Response.Headers.Add("X-Frame-Options", "DENY");
                ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                ctx.Response.Headers.Add("Referrer-Policy", "no-referrer");
                ctx.Response.Headers.Add("Feature-Policy", "camera 'none'; microphone 'none'; speaker 'self'; vibrate 'none'; geolocation 'none'; accelerometer 'none'; ambient-light-sensor 'none'; autoplay 'none'; encrypted-media 'none'; gyroscope 'none'; magnetometer 'none'; midi 'none'; payment 'none'; picture-in-picture 'none'; usb 'none'; vr 'none'; fullscreen *");
                await next();
            });

            app.UseCors("AllowedOrigins");

            app.UseRouting();

            // After UseRouting so the endpoint's [NoLog] metadata is resolved.
            if (!env.IsDevelopment())
            {
                app.UseHttpLogging();
            }

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller}/{action=Index}/{id?}");
            });

            if (!env.IsDevelopment())
            {
                app.UseSpaStaticFiles();
            }

            app.UseSpa(spa =>
            {
                // To learn more about options for serving an Angular SPA from ASP.NET Core,
                // see https://go.microsoft.com/fwlink/?linkid=864501

                spa.Options.SourcePath = "ClientApp";

                if (env.IsDevelopment())
                {
                    var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
                    var clientAppPath = System.IO.Path.Combine(env.ContentRootPath, spa.Options.SourcePath);
                    spa.UseProxyToSpaDevelopmentServer(() => AngularDevServer.EnsureStarted(
                        clientAppPath, spa.Options.StartupTimeout, lifetime.ApplicationStopping));
                }
            });
        }

        // ASCII letters, digits and - _ . : only, so a hostile header can't break response headers or log scopes.
        private static bool IsValidInboundCorrelationId(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (value.Length > CorrelationConstants.MaxInboundLength) return false;
            foreach (var c in value)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                      || c == '-' || c == '_' || c == '.' || c == ':';
                if (!ok) return false;
            }
            return true;
        }
    }
}
