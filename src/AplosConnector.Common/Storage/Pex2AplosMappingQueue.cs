using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;
using Microsoft.Azure.Storage.Queue;
using Newtonsoft.Json;

namespace AplosConnector.Common.Storage
{
    public class Pex2AplosMappingQueue : AzureQueueAbstract
    {
        public const string QUEUE_NAME = "pex-aplos-mapping";

        public Pex2AplosMappingQueue(string connectionString) : base(connectionString, QUEUE_NAME) { }

        public async Task EnqueueMapping(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var message = new CloudQueueMessage(JsonConvert.SerializeObject(mapping));
            await Queue.AddMessageAsync(message, cancellationToken);
        }
    }
}
