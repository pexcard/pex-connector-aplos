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
using System.Threading;

namespace AplosConnector.Common.Services.Abstractions
{
    public interface IAplosIntegrationService
    {
        Task<string> GetAplosAccessToken(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<bool> ValidateAplosApiCredentials(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<PexAplosApiObject> GetAplosAccount(Pex2AplosMappingModel mapping, decimal aplosAccountNumber, CancellationToken cancellationToken);
        Task<IEnumerable<PexAplosApiObject>> GetAplosAccounts(Pex2AplosMappingModel mapping, string aplosAccountCategory = null, CancellationToken cancellationToken = default);
        Task<PexAplosApiObject> GetAplosContact(Pex2AplosMappingModel mapping, int aplosContactId, CancellationToken cancellationToken);
        Task<IEnumerable<PexAplosApiObject>> GetAplosContacts(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<PexAplosApiObject> GetAplosFund(Pex2AplosMappingModel mapping, int aplosFundId, CancellationToken cancellationToken);
        Task<IEnumerable<PexAplosApiObject>> GetAplosFunds(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<IEnumerable<PexAplosApiObject>> GetAplosTagCategories(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<List<AplosApiTransactionDetail>> GetTransactions(Pex2AplosMappingModel mapping, DateTime startDate, CancellationToken cancellationToken);
        IAplosApiClient MakeAplosApiClient(Pex2AplosMappingModel mapping, AplosAuthenticationMode? overrideAuthenticationMode = null);
        Task Sync(Pex2AplosMappingModel mapping, ILogger log, CancellationToken cancellationToken);
        Task<TransactionSyncResult> SyncTransaction(IEnumerable<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)> allocationDetails, Pex2AplosMappingModel mapping, TransactionModel transaction, CardholderDetailsModel cardholderDetails, CancellationToken cancellationToken);
        Task<Pex2AplosMappingModel> EnsureMappingInstalled(PexOAuthSessionModel session, CancellationToken cancellationToken);
    }
}