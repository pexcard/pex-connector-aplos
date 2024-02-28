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
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
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
using System.Net;
using System.Net.Http;
using AplosConnector.Common.Storage;
using AplosConnector.Common.VendorCards;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Azure.Storage.Queues;
using Azure.Identity;

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
            services
                .AddControllers()
                .AddNewtonsoftJson();

            var applicationInsightsKey = _configuration.GetValue<string>("ApplicationInsightsKey");
            services.AddApplicationInsightsTelemetry(applicationInsightsKey);

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

            var storageConnectionString = _configuration.GetConnectionString("StorageConnectionString");
            
            var tableServiceClient = new TableServiceClient(storageConnectionString);
            services.TryAddSingleton(tableServiceClient);

            var pexOAuthSessionTableClient = tableServiceClient.GetTableClient(PexOAuthSessionStorage.TABLE_NAME);
            pexOAuthSessionTableClient.CreateIfNotExistsAsync();
            services.AddSingleton(_ => new PexOAuthSessionStorage(pexOAuthSessionTableClient));

            var pex2AplosMappingTableClient = tableServiceClient.GetTableClient(Pex2AplosMappingStorage.TABLE_NAME);
            pex2AplosMappingTableClient.CreateIfNotExistsAsync();
            services.AddSingleton(provider => new Pex2AplosMappingStorage(pex2AplosMappingTableClient, 
                provider.GetService<IStorageMappingService>(), provider.GetService<ILogger<Pex2AplosMappingStorage>>()));

            var syncHistoryTableClient = tableServiceClient.GetTableClient(SyncHistoryStorage.TABLE_NAME);
            syncHistoryTableClient.CreateIfNotExistsAsync();
            services.AddSingleton(_ => new SyncHistoryStorage(syncHistoryTableClient));

            var vendorCardTableClient = tableServiceClient.GetTableClient(VendorCardStorage.TABLE_NAME);
            vendorCardTableClient.CreateIfNotExistsAsync();
            services.AddSingleton<IVendorCardStorage>(provider => new VendorCardStorage(vendorCardTableClient,
                provider.GetService<IPexApiClient>(), provider.GetService<ILogger<VendorCardStorage>>()));

            var queueServiceClient = new QueueServiceClient(storageConnectionString);
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
                provider.GetService<IVendorCardStorage>()));

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

            var storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(appSettings.DataProtectionBlobContainer);
            blobContainer.CreateIfNotExistsAsync();

            services
                .AddDataProtection()
                .SetApplicationName(appSettings.DataProtectionApplicationName)
                .PersistKeysToAzureBlobStorage(
                    blobContainer,
                    appSettings.DataProtectionBlobName)
                .ProtectKeysWithAzureKeyVault(
                    new Uri(appSettings.DataProtectionKeyIdentifier),
                    new DefaultAzureCredential());


            // In production, the Angular files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "ClientApp/dist";
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
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
                    spa.UseAngularCliServer(npmScript: "start");
                }
            });
        }
    }
}
