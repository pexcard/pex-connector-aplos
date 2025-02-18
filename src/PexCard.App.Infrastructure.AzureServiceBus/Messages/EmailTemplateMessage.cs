namespace PexCard.App.Infrastructure.AzureServiceBus.Messages;

public class EmailTemplateMessage
{
    public long BusinessAcctId { get; set; }
    public string FromAddress { get; set; }
    public string ToAddress { get; set; }
    public string TemplateName { get; set; }
    public IDictionary<string, object> TemplateParams { get; set; }
}
