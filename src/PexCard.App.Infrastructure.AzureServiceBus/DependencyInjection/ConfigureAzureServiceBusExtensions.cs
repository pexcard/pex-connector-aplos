using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace PexCard.App.Infrastructure.AzureServiceBus.DependencyInjection;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using System;

public static class ServiceBusExtensions
{
    public static IServiceCollection AddAzureServiceBusSender(this IServiceCollection services, string serviceBusUrl, string topicName)
    {
        if (string.IsNullOrEmpty(serviceBusUrl))
        {
            throw new ArgumentException($"'{nameof(serviceBusUrl)}' cannot be null or empty.", nameof(serviceBusUrl));
        }

        if (string.IsNullOrEmpty(topicName))
        {
            throw new ArgumentNullException(nameof(topicName));
        }

    #if DEBUG
        //topicName += "-dev";
    #endif

        services.AddSingleton(_ => new ServiceBusClient(serviceBusUrl, new DefaultAzureCredential()));
        services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>().CreateSender(topicName));
        services.AddSingleton<IAzureServiceBusSender, AzureServiceBusSender>();

        return services;
    }
}
