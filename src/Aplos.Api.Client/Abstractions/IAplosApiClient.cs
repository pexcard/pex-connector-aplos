using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;

namespace Aplos.Api.Client.Abstractions
{
    public interface IAplosApiClient
    {
        Task<AplosApiTransactionResponse> CreateTransaction(AplosApiTransactionDetail aplosTransaction);
        Task<AplosApiAccountResponse> GetAccount(decimal aplosAccountNumber);
        Task<List<AplosApiAccountDetail>> GetAccounts(string aplosExpenseCategory = null);
        Task<AplosApiAccountListResponse> GetAccounts(int pageSize, int pageNum, string aplosExpenseCategory = null);
        Task<bool> GetAndValidateAplosAccessToken();
        Task<string> GetAplosAccessToken();
        Task<AplosApiAuthResponse> GetAuth(string aplosClientId);
        Task<AplosApiContactResponse> GetContact(int aplosContactId);
        Task<List<AplosApiContactDetail>> GetContacts();
        Task<AplosApiContactListResponse> GetContacts(int pageSize, int pageNum);
        Task<AplosApiFundResponse> GetFund(int aplosFundId);
        Task<List<AplosApiFundDetail>> GetFunds();
        Task<AplosApiFundListResponse> GetFunds(int pageSize, int pageNum);
        Task<List<AplosApiTagCategoryDetail>> GetTags();
        Task<AplosApiTagListResponse> GetTags(int pageSize, int pageNum);
        Task<AplosApiTransactionResponse> GetTransaction(int aplosTransactionId);
        Task<List<AplosApiTransactionDetail>> GetTransactions(DateTime startDate);
        Task<AplosApiTransactionListResponse> GetTransactions(DateTime startDate, int pageSize, int pageNum);
        Task<bool> IsHealthy();
    }
}