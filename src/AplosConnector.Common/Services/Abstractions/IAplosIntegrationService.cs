using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Abstractions;

namespace AplosConnector.Common.Services.Abstractions
{
    public interface IAplosIntegrationService
    {
        Task<string> GetAplosAccessToken(Pex2AplosMappingModel mapping);
        Task<PexAplosApiObject> GetAplosAccount(Pex2AplosMappingModel mapping, decimal aplosAccountNumber);
        Task<IEnumerable<PexAplosApiObject>> GetAplosAccounts(Pex2AplosMappingModel mapping, string aplosAccountCategory = null);
        Task<PexAplosApiObject> GetAplosContact(Pex2AplosMappingModel mapping, int aplosContactId);
        Task<IEnumerable<PexAplosApiObject>> GetAplosContacts(Pex2AplosMappingModel mapping);
        Task<PexAplosApiObject> GetAplosFund(Pex2AplosMappingModel mapping, int aplosFundId);
        Task<IEnumerable<PexAplosApiObject>> GetAplosFunds(Pex2AplosMappingModel mapping);
        Task<IEnumerable<PexAplosApiObject>> GetAplosTagCategories(Pex2AplosMappingModel mapping);
        Task<List<AplosApiTransactionDetail>> GetTransactions(Pex2AplosMappingModel mapping, DateTime startDate);
        IAplosApiClient MakeAplosApiClient(Pex2AplosMappingModel mapping);
        Task Sync(Pex2AplosMappingModel mapping, ILogger log);
        Task<TransactionSyncResult> SyncTransaction(IEnumerable<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)> allocationDetails, Pex2AplosMappingModel mapping, TransactionModel transaction, CardholderDetailsModel cardholderDetails);
        Task<bool> ValidateAplosApiCredentials(Pex2AplosMappingModel mapping);
    }
}