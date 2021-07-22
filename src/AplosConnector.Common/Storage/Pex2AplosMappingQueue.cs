using Newtonsoft.Json;
using AplosConnector.Common.Models;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Queue;
using System.Threading;

namespace AplosConnector.Core.Storages
{
    public class Pex2AplosMappingQueue : AzureQueueAbstract
    {
        public Pex2AplosMappingQueue(string connectionString)
            : base(connectionString, "pex-aplos-mapping") { }

        public async Task EnqueueMapping(Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            await InitQueueAsync();
            var message = new CloudQueueMessage(JsonConvert.SerializeObject(mapping));
            await Queue.AddMessageAsync(message, cancellationToken);
        }
    }
}
