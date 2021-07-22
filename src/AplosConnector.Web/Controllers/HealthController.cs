using Microsoft.AspNetCore.Mvc;
using AplosConnector.Core.Storages;
using System;
using System.Threading.Tasks;
using PexCard.Api.Client.Core;
using System.Threading;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public class HealthController : Controller
    {
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly IPexApiClient _pexApiClient;

        public HealthController(
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            IPexApiClient pexApiClient)
        {
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _pexApiClient = pexApiClient;
        }

        [HttpGet, Route("Check")]
        public async Task<HealthCheckResultModel> GetCheck(CancellationToken cancellationToken)
        {
            bool aplosResult; string aplosBaseUri = string.Empty;
            try
            {
                //aplosResult = await _aplosApiClient.IsHealthy();
                aplosResult = true; //TODO
                //aplosBaseUri = _aplosApiClient.BaseUri;
            }
            catch
            {
                aplosResult = false;
            }


            bool pexResult; string pexBaseUri = string.Empty;
            try
            {
                pexResult = await _pexApiClient.Ping(cancellationToken);
                pexBaseUri = _pexApiClient.BaseUri.ToString();
            }
            catch
            {
                pexResult = false;
            }

            var storageResult = true;
            try
            {
                await _pex2AplosMappingStorage.GetAllMappings(cancellationToken);
            }
            catch
            {
                storageResult = false;
            }

            var result = new HealthCheckResultModel()
            {
                CurrentDateTime = DateTime.UtcNow,
                AplosApiAvailable = aplosResult,
                PexApiAvailable = pexResult,
                StorageAvailable = storageResult,
                PexBaseUri = pexBaseUri,
                AplosBaseUri = aplosBaseUri
            };

            return result;
        }

        [HttpGet, Route("Ping")]
        public PingResultModel GetPing()
        {
            var result = new PingResultModel
            {
                CurrentDateTime = DateTime.UtcNow
            };
            return result;
        }

        public class HealthCheckResultModel
        {
            public DateTime CurrentDateTime { get; set; }
            public bool AplosApiAvailable { get; set; }
            public string AplosBaseUri { get; set; }
            public bool PexApiAvailable { get; set; }
            public string PexBaseUri { get; set; }
            public bool StorageAvailable { get; set; }
        }

        public class PingResultModel
        {
            public DateTime CurrentDateTime { get; set; }
        }
    }
}
