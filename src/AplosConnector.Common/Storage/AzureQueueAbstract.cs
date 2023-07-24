using Azure.Storage.Queues;

namespace AplosConnector.Common.Storage;

public class AzureQueueAbstract
{
    protected readonly QueueClient QueueClient;

    public AzureQueueAbstract(QueueClient queueClient)
    {
        QueueClient = queueClient;
    }
}