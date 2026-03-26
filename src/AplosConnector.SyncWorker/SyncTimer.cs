using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using AplosConnector.Common.Storage;
using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker;
using PexCard.Api.Client.Core.Models;

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

        [Function("SyncTimer")]
        public async Task Run(
            [TimerTrigger("0 16 3 * * *", RunOnStartup = false)]TimerInfo myTimer,
            FunctionContext context,
            CancellationToken cancellationToken)
        {
            var log = context.GetLogger<SyncTimer>();
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.UtcNow}");

            var mappings = await _mappingStorage.GetAllMappings(cancellationToken);
            log.LogInformation($"Found {mappings.Count()} mappings.");

            foreach (var mapping in mappings?.ToList().ToShuffled())
            {
                if (mapping.AutomaticSync)
                {
                    // Migrate legacy credit businesses: SyncInvoices used to gate everything.
                    // Now that purchases and fees have their own toggles, auto-enable them
                    // so existing behavior is preserved.
                    if (mapping.PEXFundingSource == FundingSource.Credit
                        && mapping.SyncInvoices
                        && !mapping.SyncTransactions
                        && !mapping.SyncPexFees)
                    {
                        mapping.SyncTransactions = true;
                        mapping.SyncPexFees = true;
                        await _mappingStorage.UpdateAsync(mapping, cancellationToken);
                        log.LogInformation($"Migrated credit business {mapping.PEXBusinessAcctId}: enabled SyncTransactions and SyncPexFees.");
                    }

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
