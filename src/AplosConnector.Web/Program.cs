using System;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

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

            })
            .UseStartup<Startup>();
}