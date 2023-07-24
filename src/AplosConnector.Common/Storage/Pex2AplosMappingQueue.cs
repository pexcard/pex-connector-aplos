using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;
using Azure.Storage.Queues;
using Newtonsoft.Json;

namespace AplosConnector.Common.Storage;

public class Pex2AplosMappingQueue : AzureQueueAbstract
{
    public const string QUEUE_NAME = "pex-aplos-mapping-vp";

    public Pex2AplosMappingQueue(QueueClient queueClient) : base(queueClient) {}

    public async Task EnqueueMapping(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
    {
        var message = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(mapping)));
        await QueueClient.SendMessageAsync(message, cancellationToken);
    }
}