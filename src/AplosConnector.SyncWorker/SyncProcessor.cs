using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using System.Threading.Tasks;
using AplosConnector.Common.Services.Abstractions;
using System;

namespace AplosConnector.SyncWorker
{
    public class SyncProcessor
    {
        private readonly IAplosIntegrationService _aplosIntegrationService;

        public SyncProcessor(
            IAplosIntegrationService aplosIntegrationService)
        {
            _aplosIntegrationService = aplosIntegrationService;
        }

        [FunctionName(nameof(SyncProcessor))]
        public async Task Run(
            [QueueTrigger("pex-aplos-mapping", Connection = "StorageConnectionString")]Pex2AplosMappingModel mapping,
            ILogger log)
        {
            log.LogInformation($"Beginning Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");

            try
            {
                await _aplosIntegrationService.Sync(mapping, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error running Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");
            }

            log.LogInformation($"Completed Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");
        }
    }
}
