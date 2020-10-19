using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using AplosConnector.Core.Storages;
using System.Linq;

namespace AplosConnector.SyncWorker
{
    public class SyncTimer
    {
        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly Pex2AplosMappingQueue _mappingQueue;

        public SyncTimer(Pex2AplosMappingStorage mappingStorage, Pex2AplosMappingQueue mappingQueue)
        {
            _mappingStorage = mappingStorage;
            _mappingQueue = mappingQueue;
        }

        [FunctionName("SyncTimer")]
        public async Task Run([TimerTrigger("0 16 3 * * *")]TimerInfo myTimer, ILogger log)
        {
            var mappings = await _mappingStorage.GetAllMappings();
            foreach (var mapping in mappings)
            {
                await _mappingQueue.EnqueueMapping(mapping);
                await Task.Delay(5000);
            }
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.UtcNow}; Found {mappings.Count()} mappings.");
        }
    }
}
