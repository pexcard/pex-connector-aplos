using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Models;
using AplosConnector.Common.Services.Abstractions;
using AplosConnector.Common.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AplosConnector.SyncWorker
{
    public class ValidateAplosAccountIds
    {
        public const string FUNCTION_NAME = nameof(ValidateAplosAccountIds);

        private readonly Pex2AplosMappingStorage _mappingStorage;
        private readonly IAplosIntegrationService _aplosIntegrationService;

        public ValidateAplosAccountIds(
            Pex2AplosMappingStorage mappingStorage,
            IAplosIntegrationService aplosIntegrationService)
        {
            _mappingStorage = mappingStorage;
            _aplosIntegrationService = aplosIntegrationService;
        }

        [Function(FUNCTION_NAME)]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            FunctionContext context,
            CancellationToken cancellationToken)
        {
            var log = context.GetLogger<ValidateAplosAccountIds>();
            log.LogInformation($"Starting function {FUNCTION_NAME}");

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

            log.LogInformation($"Found {mappings.Count()} business(es) to process");

            response.BusinessesFound = mappings.Count();

            foreach (Pex2AplosMappingModel mapping in mappings)
            {
                using (log.BeginScope($"{nameof(mapping.PEXBusinessAcctId)}{nameof(mapping.AplosAccountId)}", mapping.PEXBusinessAcctId, mapping.AplosAccountId))
                {
                    log.LogInformation("Starting to process business");
                    if (string.IsNullOrWhiteSpace(mapping.AplosAccountId))
                    {
                        log.LogInformation($"Skipping because business does not have a value for {nameof(mapping.AplosAccountId)}");
                        continue;
                    }
                    try
                    {
                        IAplosApiClient aplosClient = _aplosIntegrationService.MakeAplosApiClient(mapping, AplosAuthenticationMode.PartnerAuthentication);

                        AplosApiPartnerVerificationResponse aplosResponse = null;
                        try
                        {
                            aplosResponse = await aplosClient.GetPartnerVerification();
                            log.LogInformation($"Received partner verification response: {JsonConvert.SerializeObject(aplosResponse)}");
                        }
                        catch (AplosApiException ex) when (ex.AplosApiError.Status == StatusCodes.Status422UnprocessableEntity)
                        {
                            //Expected if they aren't verified yet
                        }

                        if (aplosResponse?.Data == null)
                        {
                            log.LogWarning($"Skipping because {nameof(aplosResponse.Data)} is null");
                            continue;
                        }
                        if (mapping.AplosPartnerVerified != aplosResponse.Data.PartnerVerification.Authorized)
                        {
                            if (mapping.AplosPartnerVerified && !aplosResponse.Data.PartnerVerification.Authorized && !overwriteEnabled)
                            {
                                log.LogInformation($"Skipping because changing {nameof(mapping.AplosPartnerVerified)} from {mapping.AplosPartnerVerified} to {aplosResponse.Data.PartnerVerification.Authorized} is not enabled");
                                continue;
                            }

                            log.LogInformation($"Updating {nameof(mapping.AplosPartnerVerified)} from '{mapping.AplosPartnerVerified}' to '{aplosResponse.Data.PartnerVerification.Authorized}'");

                            mapping.AplosPartnerVerified = aplosResponse.Data.PartnerVerification.Authorized;
                            await _mappingStorage.UpdateAsync(mapping, cancellationToken);
                            response.BusinessesUpdated++;
                        }

                        log.LogInformation("Finished processing business");
                    }
                    catch (Exception ex)
                    {
                        response.BusinessesErrored++;
                        log.LogWarning(ex, "Error processing business");
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
