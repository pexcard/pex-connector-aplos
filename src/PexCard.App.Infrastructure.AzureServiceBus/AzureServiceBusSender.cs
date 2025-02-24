using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PexCard.App.Infrastructure.AzureServiceBus.Messages;

namespace PexCard.App.Infrastructure.AzureServiceBus;

public class AzureServiceBusSender(ILogger<AzureServiceBusSender> logger, ServiceBusSender sender)
    : IAzureServiceBusSender
{

    private readonly ILogger<AzureServiceBusSender> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ServiceBusSender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    public async Task SendMessageAsync(EmailTemplateMessage emailTemplateMessage)
    {
        _logger.LogInformation("Sending email template message... ");

        var body = JsonConvert.SerializeObject(emailTemplateMessage);

        var serviceBusMessage = new ServiceBusMessage(body)
        {
            Subject = MessageSubjects.TokenExpirationEmail
        };

        await _sender.SendMessageAsync(serviceBusMessage);

        _logger.LogInformation("Sending email template message... ");
    }
}
