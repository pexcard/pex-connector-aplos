using System;
using Azure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

#if !DEBUG
using PexCard.Shared.Encryption.AspNetCore;
using PexCard.Shared.Encryption.AspNetCore.Extensions;
#endif

namespace AplosConnector.Web;

public class Program
{
    public static void Main(string[] args)
    {
        CreateWebHostBuilder(args).Build().Run();
    }

    public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, cb) =>
            {
#if !DEBUG
                    if (!cb.ProtectConfigurationSources(new ProtectConfigurationSourcesOptions()
                    {
                        AssemblyTypeMarker = typeof(Program)
                    }))
                    {
                        Environment.Exit(0);
                    }
#endif

                // KeyVault:Uri comes from the app's KeyVault__Uri setting, never git; the vault supplies TelemetryConfig--Exporter--BaseUrl.
                var kvUri = cb.Build()["KeyVault:Uri"];
                if (!string.IsNullOrWhiteSpace(kvUri))
                {
                    cb.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
                }
            })
            .UseStartup<Startup>();
}