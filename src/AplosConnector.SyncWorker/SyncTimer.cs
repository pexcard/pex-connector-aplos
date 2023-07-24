using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using AplosConnector.Common.Storage;
using System.Collections.Generic;

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
        public async Task Run([TimerTrigger("0 16 3 * * *")]TimerInfo myTimer, CancellationToken cancellationToken, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.UtcNow}");

            var mappings = await _mappingStorage.GetAllMappings(cancellationToken);
            log.LogInformation($"Found {mappings.Count()} mappings.");

            foreach (var mapping in mappings?.ToList().ToShuffled())
            {
                if (mapping.AutomaticSync)
                {
                    log.LogInformation($"Enqueuing {nameof(mapping.PEXBusinessAcctId)} '{mapping.PEXBusinessAcctId}'");
                    await _mappingQueue.EnqueueMapping(mapping, cancellationToken);
                }
                else
                {
                    log.LogInformation($"Skipping {nameof(mapping.PEXBusinessAcctId)} '{mapping.PEXBusinessAcctId}'. AutomaticSync is off.");
                }
            }

            log.LogInformation($"C# Timer trigger function finished at: {DateTime.UtcNow}");
        }
    }
}
