using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.VendorCards;

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
        Task<IEnumerable<PexAplosApiObject>> GetAplosTags(Pex2AplosMappingModel mapping, string categoryId, CancellationToken cancellationToken);
        Task<List<AplosApiTransactionDetail>> GetTransactions(Pex2AplosMappingModel mapping, DateTime startDate, CancellationToken cancellationToken);
        IAplosApiClient MakeAplosApiClient(Pex2AplosMappingModel mapping, AplosAuthenticationMode? overrideAuthenticationMode = null);
        Task Sync(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<TransactionSyncResult> SyncTransaction(IEnumerable<(AllocationTagValue allocation, PexTagValuesModel pexTagValues)> allocationDetails, Pex2AplosMappingModel mapping, TransactionModel transaction, CardholderDetailsModel cardholderDetails, List<VendorCardOrdered> vendorCardsOrdered, CancellationToken cancellationToken);
        Task<Pex2AplosMappingModel> EnsureMappingInstalled(PexOAuthSessionModel session, CancellationToken cancellationToken);
        Task<IEnumerable<AplosApiTaxTagCategoryDetail>> GetAplosApiTaxTagExpenseCategoryDetails(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<Pex2AplosMappingModel> UpdateFundingSource(Pex2AplosMappingModel mapping, CancellationToken cancellationToken);
        Task<AplosApiPayablesListResponse> GetAplosPayables(Pex2AplosMappingModel mapping, DateTime startDate, CancellationToken cancellationToken);
    }
}