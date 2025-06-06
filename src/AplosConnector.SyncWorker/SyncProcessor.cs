using Microsoft.Extensions.Logging;
using AplosConnector.Common.Models;
using System.Threading.Tasks;
using AplosConnector.Common.Services.Abstractions;
using System;
using System.Threading;
using AplosConnector.Common.Storage;
using Microsoft.Azure.Functions.Worker;

namespace AplosConnector.SyncWorker
{
    public class SyncProcessor
    {
        private readonly IAplosIntegrationService _aplosIntegrationService;

        public SyncProcessor(IAplosIntegrationService aplosIntegrationService)
        {
            _aplosIntegrationService = aplosIntegrationService;
        }

        [Function(nameof(SyncProcessor))]
        public async Task Run(
            [QueueTrigger(Pex2AplosMappingQueue.QUEUE_NAME)] Pex2AplosMappingModel mapping,
            FunctionContext context,
            CancellationToken cancellationToken)
        {
            var logger = context.GetLogger<SyncProcessor>();
            logger.LogInformation($"Beginning Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");

            try
            {
                await _aplosIntegrationService.Sync(mapping, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error running Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");
            }

            logger.LogInformation($"Completed Azure Function {nameof(SyncProcessor)} for {nameof(mapping.PEXBusinessAcctId)} {mapping.PEXBusinessAcctId}.");
        }
    }
}
