using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using AplosConnector.Common.Models;
using System.Linq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Models;
using System.Threading;
using AplosConnector.Common.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace AplosConnector.SyncWorker
{
    public class PopulateAplosAccountIds
    {
        public const string FUNCTION_NAME = nameof(PopulateAplosAccountIds);

        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly IPexApiClient _pexApiClient;

        public PopulateAplosAccountIds(
            Pex2AplosMappingStorage mappingStorage,
            IPexApiClient pexApiClient)
        {
            _mappingStorage = mappingStorage;
            _pexApiClient = pexApiClient;
        }

        [Function(FUNCTION_NAME)]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function,"post", Route = null)] HttpRequest req,
            FunctionContext context,
            CancellationToken cancellationToken)
        {
            var log = context.GetLogger<PopulateAplosAccountIds>();
            log.LogInformation($"Starting function {FUNCTION_NAME}");

            bool overwriteEnabled;
            bool.TryParse(req.Query[nameof(overwriteEnabled)], out overwriteEnabled);

            int overridePexBusinessAcctId;
            int.TryParse(req.Query[nameof(overridePexBusinessAcctId)], out overridePexBusinessAcctId);

            var response = new PopulateAplosAccountIdsResponse();

            IEnumerable<Pex2AplosMappingModel> mappings;
            if (overridePexBusinessAcctId != default)
            {
                var mapping = await _mappingStorage.GetByBusinessAcctIdAsync(overridePexBusinessAcctId, cancellationToken);
                if (mapping == null)
                {
                    return new NotFoundObjectResult(response);
                }

                mappings = new[] { mapping, };
            }
            else
            {
                mappings = await _mappingStorage.GetAllMappings(cancellationToken);
            }


            log.LogInformation($"Found {mappings.Count()} business(es) to process");

            response.BusinessesFound = mappings.Count();

            foreach (Pex2AplosMappingModel mapping in mappings)
            {
                using (log.BeginScope($"{nameof(mapping.PEXBusinessAcctId)}{nameof(mapping.AplosAccountId)}", mapping.PEXBusinessAcctId, mapping.AplosAccountId))
                {
                    log.LogInformation($"Starting to process business");

                    try
                    {
                        PartnerModel partnerInfo = await _pexApiClient.GetPartner(mapping.PEXExternalAPIToken, cancellationToken);
                        using (log.BeginScope($"{nameof(partnerInfo.PartnerName)}{nameof(partnerInfo.PartnerBusinessId)}", partnerInfo.PartnerName, partnerInfo.PartnerBusinessId))
                        {
                            log.LogInformation($"Got partner info");

                            if (mapping.AplosAccountId != partnerInfo.PartnerBusinessId)
                            {
                                if (!string.IsNullOrWhiteSpace(mapping.AplosAccountId) && !overwriteEnabled)
                                {
                                    log.LogInformation($"Skipping because {nameof(mapping.AplosAccountId)} already has a value");
                                    continue;
                                }

                                log.LogInformation($"Updating {nameof(mapping.AplosAccountId)} from '{mapping.AplosAccountId}' to '{partnerInfo.PartnerBusinessId}'");

                                mapping.AplosAccountId = partnerInfo.PartnerBusinessId;
                                await _mappingStorage.UpdateAsync(mapping, cancellationToken);
                                response.BusinessesUpdated++;
                            }
                            log.LogInformation("Finished processing business");
                        }
                    }
                    catch (Exception ex)
                    {
                        response.BusinessesErrored++;
                        log.LogWarning(ex, $"Error processing business");
                    }
                }
            }

            return new OkObjectResult(response);
        }

        private class PopulateAplosAccountIdsResponse
        {
            public int BusinessesFound { get; set; }
            public int BusinessesUpdated { get; set; }
            public int BusinessesErrored { get; set; }
        }
    }
}
