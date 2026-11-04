using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;
using AplosConnector.Common.Tracing;
using Azure.Storage.Queues;

namespace AplosConnector.Common.Storage;

public class Pex2AplosMappingQueue : AzureQueueAbstract
{
#if DEBUG
    public const string QUEUE_NAME = "pex-aplos-mapping-vp";
#else
        public const string QUEUE_NAME = "pex-aplos-mapping";
#endif

    public Pex2AplosMappingQueue(QueueClient queueClient) : base(queueClient) {}

    public async Task EnqueueMapping(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
    {
        await QueueClient.SendMessageWithTraceContextAsync(mapping, cancellationToken);
    }
}