using PexCard.App.Infrastructure.AzureServiceBus.Messages;

namespace PexCard.App.Infrastructure.AzureServiceBus;

public interface IAzureServiceBusSender
{
    public Task SendMessageAsync(EmailTemplateMessage emailTemplateMessage);
}
