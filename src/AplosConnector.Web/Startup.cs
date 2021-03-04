using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aplos.Api.Client;
using AplosConnector.Common.Models.Settings;
using AplosConnector.Core.Storages;
using PexCard.Api.Client;
using PexCard.Api.Client.Core;
using Polly;
using Polly.Extensions.Http;
using Microsoft.Extensions.Hosting;
using AplosConnector.Common.Services;
using AplosConnector.Common.Services.Abstractions;
using Aplos.Api.Client.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

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
            // In production, the Angular files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "ClientApp/dist";
            });

            services.AddOptions();

            var configSection = _configuration.GetSection("AppSettings");
            var appSettings = configSection.Get<AppSettingsModel>();
            services.Configure<AppSettingsModel>(configSection);

            services.AddHttpClient();
            services.AddHttpClient<IPexApiClient, PexApiClient>((client) =>
            {
                client.BaseAddress = appSettings.PEXAPIBaseURL;
            })
            .AddPolicyHandler(GetPexRetryPolicy());

            services.AddScoped<IStorageMappingService>(
                provider => new StorageMappingService(
                    provider.GetService<IDataProtectionProvider>()
                ));

            string storageConnectionString = _configuration.GetConnectionString("StorageConnectionString");

            services.AddScoped(provider => new PexOAuthSessionStorage(storageConnectionString));
            services.AddScoped(provider =>
                new Pex2AplosMappingStorage(
                    storageConnectionString,
                    provider.GetService<IStorageMappingService>(),
                    provider.GetService<ILogger<Pex2AplosMappingStorage>>()
                ));
            services.AddScoped(provider => new SyncResultStorage(storageConnectionString));
            services.AddScoped(provider => new Pex2AplosMappingQueue(storageConnectionString));

            services.AddScoped<IAccessTokenDecryptor>(provider => new AplosAccessTokenDecryptor());

            services.AddScoped<IAplosApiClientFactory>(provider => new AplosApiClientFactory(
                provider.GetService<IHttpClientFactory>(),
                provider.GetService<IAccessTokenDecryptor>(),
                provider.GetService<ILogger<AplosApiClientFactory>>()));

            services.AddScoped<IAplosIntegrationMappingService>(provider => new AplosIntegrationMappingService());
            services.AddScoped<IAplosIntegrationService>(provider => new AplosIntegrationService(
                provider.GetService<IOptions<AppSettingsModel>>(),
                provider.GetService<IAplosApiClientFactory>(),
                provider.GetService<IAplosIntegrationMappingService>(),
                provider.GetService<IPexApiClient>(),
                provider.GetService<SyncResultStorage>(),
                provider.GetService<Pex2AplosMappingStorage>()));

            //var appSettings = services.BuildServiceProvider().GetService<IOptions<AppSettingsModel>>().Value;
            

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
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer blobContainer = blobClient.GetContainerReference(appSettings.DataProtectionBlobContainer);
            blobContainer.CreateIfNotExistsAsync();

            var keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(new AzureServiceTokenProvider().KeyVaultTokenCallback));

            services
                .AddDataProtection()
                .SetApplicationName(appSettings.DataProtectionApplicationName)
                .PersistKeysToAzureBlobStorage(
                    blobContainer,
                    appSettings.DataProtectionBlobName)
                .ProtectKeysWithAzureKeyVault(
                    keyVaultClient,
                    appSettings.DataProtectionKeyIdentifier);

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            //services
            //    .AddControllersWithViews()
            //    .AddNewtonsoftJson();

            var applicationInsightsKey = _configuration.GetValue<string>("ApplicationInsightsKey");
            services.AddApplicationInsightsTelemetry(applicationInsightsKey);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, Microsoft.AspNetCore.Hosting.IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            if (!env.IsDevelopment())
            {
                app.UseSpaStaticFiles();
            }

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

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller}/{action=Index}/{id?}");
            });

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

        private static IAsyncPolicy<HttpResponseMessage> GetPexRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .RetryAsync(3);
        }
    }
}
