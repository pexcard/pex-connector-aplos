using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using AplosConnector.Common.Models;
using System.Linq;
using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models.Response;
using Newtonsoft.Json;
using AplosConnector.Common.Services.Abstractions;
using Aplos.Api.Client.Exceptions;
using System.Threading;
using AplosConnector.Common.Storage;

namespace AplosConnector.SyncWorker
{
    public class ValidateAplosAccountIds
    {
        public const string FUNCTION_NAME = nameof(ValidateAplosAccountIds);

        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly IAplosIntegrationService _aplosIntegrationService;
        private readonly ILogger _logger;

        public ValidateAplosAccountIds(
            Pex2AplosMappingStorage mappingStorage,
            IAplosIntegrationService aplosIntegrationService,
            ILogger<PopulateAplosAccountIds> logger)
        {
            _mappingStorage = mappingStorage;
            _aplosIntegrationService = aplosIntegrationService;
            _logger = logger;
        }

        [FunctionName(FUNCTION_NAME)]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Starting function {FUNCTION_NAME}");

            bool overwriteEnabled;
            bool.TryParse(req.Query[nameof(overwriteEnabled)], out overwriteEnabled);

            int overridePexBusinessAcctId;
            int.TryParse(req.Query[nameof(overridePexBusinessAcctId)], out overridePexBusinessAcctId);

            var response = new ValidateAplosAccountIdsResponse();

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

            _logger.LogInformation($"Found {mappings.Count()} business(es) to process");

            response.BusinessesFound = mappings.Count();

            foreach (Pex2AplosMappingModel mapping in mappings)
            {
                using (_logger.BeginScope($"{nameof(mapping.PEXBusinessAcctId)}{nameof(mapping.AplosAccountId)}", mapping.PEXBusinessAcctId, mapping.AplosAccountId))
                {
                    _logger.LogInformation($"Starting to process business");

                    if (string.IsNullOrWhiteSpace(mapping.AplosAccountId))
                    {
                        _logger.LogInformation($"Skipping because business does not have a value for {nameof(mapping.AplosAccountId)}");
                        continue;
                    }

                    try
                    {
                        IAplosApiClient aplosClient = _aplosIntegrationService.MakeAplosApiClient(mapping, AplosAuthenticationMode.PartnerAuthentication);

                        AplosApiPartnerVerificationResponse aplosResponse = null;
                        try
                        {
                            aplosResponse = await aplosClient.GetPartnerVerification();
                            _logger.LogInformation($"Received partner verification response: {JsonConvert.SerializeObject(aplosResponse)}");
                        }
                        catch (AplosApiException ex) when (ex.AplosApiError.Status == StatusCodes.Status422UnprocessableEntity)
                        {
                            //Expected if they aren't verified yet
                        }

                        if (aplosResponse?.Data == null)
                        {
                            _logger.LogWarning($"Skipping because {nameof(aplosResponse.Data)} is null");
                            continue;
                        }

                        if (mapping.AplosPartnerVerified != aplosResponse.Data.PartnerVerification.Authorized)
                        {
                            if (mapping.AplosPartnerVerified && !aplosResponse.Data.PartnerVerification.Authorized && !overwriteEnabled)
                            {
                                _logger.LogInformation($"Skipping because changing {nameof(mapping.AplosPartnerVerified)} from {mapping.AplosPartnerVerified} to {aplosResponse.Data.PartnerVerification.Authorized} is not enabled");
                                continue;
                            }

                            _logger.LogInformation($"Updating {nameof(mapping.AplosPartnerVerified)} from '{mapping.AplosPartnerVerified}' to '{aplosResponse.Data.PartnerVerification.Authorized}'");

                            mapping.AplosPartnerVerified = aplosResponse.Data.PartnerVerification.Authorized;
                            await _mappingStorage.UpdateAsync(mapping, cancellationToken);

                            response.BusinessesUpdated++;
                        }

                        _logger.LogInformation($"Finished processing business");
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

        private class ValidateAplosAccountIdsResponse
        {
            public int BusinessesFound { get; set; }
            public int BusinessesUpdated { get; set; }
            public int BusinessesErrored { get; set; }
        }
    }
}
