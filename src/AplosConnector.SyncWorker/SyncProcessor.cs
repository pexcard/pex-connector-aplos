using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using System.Threading.Tasks;
using AplosConnector.Common.Services.Abstractions;
using System;
using System.Threading;

namespace AplosConnector.SyncWorker
{
    public class SyncProcessor
    {
        private readonly ILogger _logger;
        private readonly IAplosIntegrationService _aplosIntegrationService;

        public SyncProcessor(ILogger<SyncProcessor> logger,
                             IAplosIntegrationService aplosIntegrationService)
        {
            _logger = logger;
            _aplosIntegrationService = aplosIntegrationService;
        }

        [FunctionName(nameof(SyncProcessor))]
        public async Task Run([QueueTrigger("pex-aplos-mapping", Connection = "StorageConnectionString")] Pex2AplosMappingModel mapping, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Beginning Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");

            try
            {
                await _aplosIntegrationService.Sync(mapping, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error running Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");
            }

            _logger.LogInformation($"Completed Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");
        }
    }
}
