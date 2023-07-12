using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;
using Microsoft.Azure.Storage.Queue;
using Newtonsoft.Json;

namespace AplosConnector.Common.Storage
{
    public class Pex2AplosMappingQueue : AzureQueueAbstract
    {
        public Pex2AplosMappingQueue(string connectionString)
            : base(connectionString, "pex-aplos-mapping") { }

        public async Task EnqueueMapping(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            var message = new CloudQueueMessage(JsonConvert.SerializeObject(mapping));
            await Queue.AddMessageAsync(message, cancellationToken);
        }
    }
}
