using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AplosConnector.Core.Storages;
using System.Collections.Generic;
using AplosConnector.Common.Models;
using System.Linq;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Models;

namespace AplosConnector.SyncWorker
{
    public class PopulateAplosAccountIds
    {
        public const string FUNCTION_NAME = nameof(PopulateAplosAccountIds);

        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly IPexApiClient _pexApiClient;
        private readonly ILogger _logger;

        public PopulateAplosAccountIds(
            Pex2AplosMappingStorage mappingStorage,
            IPexApiClient pexApiClient,
            ILogger<PopulateAplosAccountIds> logger)
        {
            _mappingStorage = mappingStorage;
            _pexApiClient = pexApiClient;
            _logger = logger;
        }

        [FunctionName(FUNCTION_NAME)]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req)
        {
            _logger.LogInformation($"Starting function {FUNCTION_NAME}");

            bool overwriteEnabled;
            bool.TryParse(req.Query[nameof(overwriteEnabled)], out overwriteEnabled);

            int overridePexBusinessAcctId;
            int.TryParse(req.Query[nameof(overridePexBusinessAcctId)], out overridePexBusinessAcctId);

            var response = new PopulateAplosAccountIdsResponse();

            IEnumerable<Pex2AplosMappingModel> mappings;
            if (overridePexBusinessAcctId != default)
            {
                var mapping = await _mappingStorage.GetByBusinessAcctIdAsync(overridePexBusinessAcctId);
                if (mapping == null)
                {
                    return new NotFoundObjectResult(response);
                }

                mappings = new[] { mapping, };
            }
            else
            {
                mappings = await _mappingStorage.GetAllMappings();
            }

            
            _logger.LogInformation($"Found {mappings.Count()} business(es) to process");

            response.BusinessesFound = mappings.Count();

            foreach (Pex2AplosMappingModel mapping in mappings)
            {
                using (_logger.BeginScope($"{nameof(mapping.PEXBusinessAcctId)}{nameof(mapping.AplosAccountId)}", mapping.PEXBusinessAcctId, mapping.AplosAccountId))
                {
                    _logger.LogInformation($"Starting to process business");

                    try
                    {
                        PartnerModel partnerInfo = await _pexApiClient.GetPartner(mapping.PEXExternalAPIToken);
                        using (_logger.BeginScope($"{nameof(partnerInfo.PartnerName)}{nameof(partnerInfo.PartnerBusinessId)}", partnerInfo.PartnerName, partnerInfo.PartnerBusinessId))
                        {
                            _logger.LogInformation($"Got partner info");

                            if (mapping.AplosAccountId != partnerInfo.PartnerBusinessId)
                            {
                                if (!string.IsNullOrWhiteSpace(mapping.AplosAccountId) && !overwriteEnabled)
                                {
                                    _logger.LogInformation($"Skipping because {nameof(mapping.AplosAccountId)} already has a value");
                                    continue;
                                }

                                _logger.LogInformation($"Updating {nameof(mapping.AplosAccountId)} from '{mapping.AplosAccountId}' to '{partnerInfo.PartnerBusinessId}'");

                                mapping.AplosAccountId = partnerInfo.PartnerBusinessId;
                                await _mappingStorage.UpdateAsync(mapping);

                                response.BusinessesUpdated++;
                            }

                            _logger.LogInformation($"Finished processing business");
                        }
                    }
                    catch (Exception ex)
                    {
                        response.BusinessesErrored++;
                        _logger.LogWarning(ex, $"Error processing business");
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
