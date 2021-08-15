using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;

namespace Aplos.Api.Client.Abstractions
{
    public interface IAplosApiClient
    {
        Task<AplosApiTransactionResponse> CreateTransaction(AplosApiTransactionDetail aplosTransaction, CancellationToken cancellationToken = default);
        Task<AplosApiAccountResponse> GetAccount(decimal aplosAccountNumber, CancellationToken cancellationToken = default);
        Task<List<AplosApiAccountDetail>> GetAccounts(string aplosExpenseCategory = null, CancellationToken cancellationToken = default);
        Task<AplosApiAccountListResponse> GetAccounts(int pageSize, int pageNum, string aplosExpenseCategory = null, CancellationToken cancellationToken = default);
        Task<bool> GetAndValidateAplosAccessToken(CancellationToken cancellationToken = default);
        Task<string> GetAplosAccessToken(CancellationToken cancellationToken = default);
        Task<AplosApiAuthResponse> GetAuth(string aplosClientId, CancellationToken cancellationToken = default);
        Task<AplosApiContactResponse> GetContact(int aplosContactId, CancellationToken cancellationToken = default);
        Task<List<AplosApiContactDetail>> GetContacts(CancellationToken cancellationToken = default);
        Task<AplosApiContactListResponse> GetContacts(int pageSize, int pageNum, CancellationToken cancellationToken = default);
        Task<AplosApiFundResponse> GetFund(int aplosFundId, CancellationToken cancellationToken = default);
        Task<List<AplosApiFundDetail>> GetFunds(CancellationToken cancellationToken = default);
        Task<AplosApiFundListResponse> GetFunds(int pageSize, int pageNum, CancellationToken cancellationToken = default);
        Task<List<AplosApiTagCategoryDetail>> GetTags(CancellationToken cancellationToken = default);
        Task<AplosApiTagListResponse> GetTags(int pageSize, int pageNum, CancellationToken cancellationToken = default);
        Task<AplosApiTransactionResponse> GetTransaction(int aplosTransactionId, CancellationToken cancellationToken = default);
        Task<List<AplosApiTransactionDetail>> GetTransactions(DateTime startDate, CancellationToken cancellationToken = default);
        Task<AplosApiTransactionListResponse> GetTransactions(DateTime startDate, int pageSize, int pageNum, CancellationToken cancellationToken = default);
        Task<bool> IsHealthy(CancellationToken cancellationToken = default);
        Task<AplosApiPartnerVerificationResponse> GetPartnerVerification(CancellationToken cancellationToken = default);
        Task<List<AplosApiTaxTagCategoryDetail>> GetTaxTags(CancellationToken cancellationToken = default);
        Task<AplosApiTaxTagListResponse> GetTaxTags(int pageSize, int pageNum, CancellationToken cancellationToken = default);
    }
}