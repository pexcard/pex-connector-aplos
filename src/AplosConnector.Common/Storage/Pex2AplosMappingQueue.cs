using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using AplosConnector.Common.Models;
using System.Threading.Tasks;

namespace AplosConnector.Core.Storages
{
    public class Pex2AplosMappingQueue : AzureQueueAbstract
    {
        public Pex2AplosMappingQueue(string connectionString)
            : base(connectionString, "pex-aplos-mapping") { }

        public async Task EnqueueMapping(Pex2AplosMappingModel mapping)
        {
            await InitQueueAsync();
            var message = new CloudQueueMessage(JsonConvert.SerializeObject(mapping));
            await Queue.AddMessageAsync(message);
        }
    }
}
