using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Immutable;
using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models.Detail;
using Aplos.Api.Client.Models.Response;
using Aplos.Api.Client.Models;

namespace Aplos.Api.Client
{
    public class AplosApiClient : IAplosApiClient
    {
        private const string APLOS_ENDPOINT_AUTH = "auth/";
        private const string APLOS_ENDPOINT_ACCOUNTS = "accounts/";
        private const string APLOS_ENDPOINT_CONTACTS = "contacts/";
        private const string APLOS_ENDPOINT_FUNDS = "funds/";
        private const string APLOS_ENDPOINT_TRANSACTIONS = "transactions/";
        private const string APLOS_ENDPOINT_TAGS = "tags/";

        private const string APLOS_ACCOUNT_CATEGORY_ASSET = "asset";
        private const string APLOS_ACCOUNT_CATEGORY_EXPENSE = "expense";
        private static readonly ImmutableHashSet<string> _validAplosAccountCategories = new HashSet<string> { APLOS_ACCOUNT_CATEGORY_ASSET, APLOS_ACCOUNT_CATEGORY_EXPENSE }.ToImmutableHashSet();

        private readonly string _aplosClientId;
        private readonly string _aplosPrivateKey;
        private readonly Uri _aplosApiEndpointUri;
        private readonly IHttpClientFactory _clientFactory;
        private readonly IAccessTokenDecryptor _accessTokenDecryptor;
        private readonly ILogger _logger;
        private readonly Func<ILogger, AplosAuthModel> _onAuthInitializing;
        private readonly Func<AplosAuthModel, ILogger, Task> _onAuthRefreshed;

        public AplosApiClient(
            string aplosClientId,
            string aplosPrivateKey,
            Uri aplosApiEndpointUri,
            IHttpClientFactory clientFactory,
            IAccessTokenDecryptor accessTokenDecryptor,
            ILogger logger,
            Func<ILogger, AplosAuthModel> onAuthInitializing = null,
            Func<AplosAuthModel, ILogger, Task> onAuthRefreshed = null)
        {
            _aplosClientId = aplosClientId;
            _aplosPrivateKey = aplosPrivateKey;
            _aplosApiEndpointUri = aplosApiEndpointUri;
            _clientFactory = clientFactory;
            _accessTokenDecryptor = accessTokenDecryptor;
            _logger = logger;
            _onAuthInitializing = onAuthInitializing;
            _onAuthRefreshed = onAuthRefreshed;
        }

        private HttpClient MakeAplosHttpClient()
        {
            HttpClient httpClient = _clientFactory.CreateClient(""); //TODO: Create strongly typed HttpClient, DI, Polly
            httpClient.BaseAddress = _aplosApiEndpointUri;

            return httpClient;
        }

        private async Task<HttpClient> MakeAuthenticatedAplosHttpClient()
        {
            var httpClient = MakeAplosHttpClient();

            string aplosAccessToken = await GetAplosAccessToken();

            //Aplos uses a nonstandard Authorization header; we have to purposely add it without validation or we'll get an exception.
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer: {aplosAccessToken}");

            return httpClient;
        }

        private async Task<TResponseContent> InvokeAplosApi<TResponseContent>(
            HttpClient httpClient,
            HttpContent httpRequestContent,
            HttpMethod httpMethod,
            string endpoint)
        {
            var httpRequest = new HttpRequestMessage(httpMethod, endpoint);

            if (httpRequestContent != null)
            {
                httpRequest.Content = httpRequestContent;
            }

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);
            string responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(responseBody);

            var errorResponse = JsonConvert.DeserializeObject<AplosApiErrorResponse>(responseBody);
            if (!response.IsSuccessStatusCode || errorResponse?.Exception != null)
            {
                var aplosApiException = new AplosApiException(errorResponse);

                _logger.LogError(aplosApiException, $"Error invoking Aplos API: {responseBody}");
                throw aplosApiException;
            }

            return JsonConvert.DeserializeObject<TResponseContent>(responseBody);
        }

        private async Task<TResponseContent> InvokeAplosApi<TResponseContent>(
            HttpMethod httpMethod,
            string endpoint)
        {
            return await InvokeAplosApi<TResponseContent>(
                MakeAplosHttpClient(),
                null,
                httpMethod,
                endpoint);
        }

        private async Task<TResponseContent> InvokeAplosApiWithAccessToken<TRequestContent, TResponseContent>(
            HttpMethod httpMethod,
            string endpoint,
            TRequestContent requestContent)
        {
            string content = JsonConvert.SerializeObject(requestContent);

            var httpRequestContent = new StringContent(
                content,
                Encoding.UTF8,
                "application/json");

            return await InvokeAplosApi<TResponseContent>(
                await MakeAuthenticatedAplosHttpClient(),
                httpRequestContent,
                httpMethod,
                endpoint);
        }

        private async Task<TResponseContent> InvokeAplosApiWithAccessToken<TResponseContent>(
            HttpMethod httpMethod,
            string endpoint)
        {
            return await InvokeAplosApi<TResponseContent>(
                await MakeAuthenticatedAplosHttpClient(),
                null,
                httpMethod,
                endpoint);
        }

        private async Task<string> DecryptEncryptedAplosAccessToken(
            string aplosPrivateKey,
            string encryptedAplosAccessToken)
        {
            return await Task.Run(() => _accessTokenDecryptor.Decrypt(aplosPrivateKey, encryptedAplosAccessToken));
        }

        public async Task<bool> GetAndValidateAplosAccessToken()
        {
            bool isValid = true;
            try
            {
                //Aplos doesn't have a clean way to verify that they will actually accept an access token that you have.
                //To get around this, we are validating by calling /accounts, but this could be replaced with calling any safe endpoint.
                await GetAplosAccessToken();
                await GetAccounts();
            }
            catch (AplosApiException ex)
            {
                _logger.LogWarning(ex, $"{nameof(GetAndValidateAplosAccessToken)}: Error getting access token");
                isValid = false;
            }

            return isValid;
        }

        public async Task<AplosApiAuthResponse> GetAuth(string aplosClientId)
        {
            return await InvokeAplosApi<AplosApiAuthResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_AUTH + aplosClientId);
        }

        public async Task<List<AplosApiAccountDetail>> GetAccounts(
            string aplosExpenseCategory = null)
        {
            //We only want enabled accounts.
            var endpoint = APLOS_ENDPOINT_ACCOUNTS + "?f_enabled=y";
            if (_validAplosAccountCategories.Contains(aplosExpenseCategory))
            {
                endpoint += $"&f_type={aplosExpenseCategory}";
            }

            var response = await InvokeAplosApiWithAccessToken<AplosApiAccountListResponse>(
                HttpMethod.Get,
                endpoint);
            var result = new List<AplosApiAccountDetail>(response.Data.Accounts);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiAccountListResponse>(
                    HttpMethod.Get, response.Links.Next.Replace("/api/v1/", ""));
                result.AddRange(response.Data.Accounts);
            }
            return result;
        }

        public async Task<AplosApiAccountListResponse> GetAccounts(
            int pageSize, int pageNum, string aplosExpenseCategory = null)
        {
            //We only want enabled accounts.
            var endpoint = $"{APLOS_ENDPOINT_ACCOUNTS}?f_enabled=y&page_size={pageSize}&page_num={pageNum}";
            if (_validAplosAccountCategories.Contains(aplosExpenseCategory))
            {
                endpoint += $"&f_type={aplosExpenseCategory}";
            }

            return await InvokeAplosApiWithAccessToken<AplosApiAccountListResponse>(
                HttpMethod.Get,
                endpoint);
        }

        public async Task<AplosApiAccountResponse> GetAccount(
            decimal aplosAccountNumber)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiAccountResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_ACCOUNTS + aplosAccountNumber);
        }

        public async Task<List<AplosApiFundDetail>> GetFunds()
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiFundListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_FUNDS);
            var result = new List<AplosApiFundDetail>(response.Data.Funds);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiFundListResponse>(
                    HttpMethod.Get, response.Links.Next.Replace("/api/v1/", ""));
                result.AddRange(response.Data.Funds);
            }
            return result;
        }

        public async Task<AplosApiFundListResponse> GetFunds(int pageSize, int pageNum)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiFundListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_FUNDS}?page_size={pageSize}&page_num={pageNum}");
        }

        public async Task<AplosApiFundResponse> GetFund(
            int aplosFundId)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiFundResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_FUNDS + aplosFundId);
        }

        public async Task<List<AplosApiContactDetail>> GetContacts()
        {
            //We will only need to sync transactions with businesses, not individuals. Assuming our client has their Aplos contacts set up correctly.
            var response = await InvokeAplosApiWithAccessToken<AplosApiContactListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_CONTACTS + "?f_type=company");
            var result = new List<AplosApiContactDetail>(response.Data.Contacts);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiContactListResponse>(
                    HttpMethod.Get, response.Links.Next.Replace("/api/v1/", ""));
                result.AddRange(response.Data.Contacts);
            }
            return result;
        }

        public async Task<AplosApiContactListResponse> GetContacts(int pageSize, int pageNum)
        {
            //We will only need to sync transactions with businesses, not individuals. Assuming our client has their Aplos contacts set up correctly.
            return await InvokeAplosApiWithAccessToken<AplosApiContactListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_CONTACTS}?f_type=company&page_size={pageSize}&page_num={pageNum}");
        }

        public async Task<AplosApiContactResponse> GetContact(
            int aplosContactId)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiContactResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_CONTACTS + aplosContactId);
        }

        public async Task<List<AplosApiTagCategoryDetail>> GetTags()
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiTagListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_TAGS);

            var result = new List<AplosApiTagCategoryDetail>(response.Data.TagCategories);

            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiTagListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""));

                result.AddRange(response.Data.TagCategories);
            }
            return result;
        }

        public async Task<AplosApiTagListResponse> GetTags(
            int pageSize, 
            int pageNum)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTagListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TAGS}?page_size={pageSize}&page_num={pageNum}");
        }

        public async Task<List<AplosApiTransactionDetail>> GetTransactions(
            DateTime startDate)
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiTransactionListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TRANSACTIONS}?f_rangestart={startDate:yyyy-MM-dd}");
            var result = new List<AplosApiTransactionDetail>(response.Data.Transactions);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiTransactionListResponse>(
                    HttpMethod.Get, response.Links.Next.Replace("/api/v1/", ""));
                result.AddRange(response.Data.Transactions);
            }
            return result;
        }

        public async Task<AplosApiTransactionListResponse> GetTransactions(
            DateTime startDate, int pageSize, int pageNum)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTransactionListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TRANSACTIONS}?page_size={pageSize}&page_num={pageNum}&f_rangestart={startDate:yyyy-MM-dd}");
        }

        public async Task<AplosApiTransactionResponse> GetTransaction(
            int aplosTransactionId)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTransactionResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_TRANSACTIONS + aplosTransactionId);
        }

        public async Task<AplosApiTransactionResponse> CreateTransaction(
            AplosApiTransactionDetail aplosTransaction)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTransactionDetail, AplosApiTransactionResponse>(
                HttpMethod.Post,
                APLOS_ENDPOINT_TRANSACTIONS,
                aplosTransaction);
        }

        public Task<bool> IsHealthy()
        {
            throw new NotImplementedException();
        }

        private AplosAuthModel _auth;
        public async Task<string> GetAplosAccessToken()
        {
            if (_auth == null && _onAuthInitializing != null)
            {
                _auth = _onAuthInitializing(_logger);
            }

            if (_auth?.AplosAccessTokenExpiresAt <= DateTime.UtcNow)
            {
                _auth = null;
                //TODO Log
            }

            if (_auth == null)
            {
                AplosApiAuthResponse authResponse = await GetAuth(_aplosClientId);
                var decryptedAplosAccessToken = await DecryptEncryptedAplosAccessToken(_aplosPrivateKey, authResponse.Data.Token);

                _auth = new AplosAuthModel
                {
                    AplosAccessToken = decryptedAplosAccessToken,
                    AplosAccessTokenExpiresAt = authResponse.Data.Expires
                };

                if (_onAuthRefreshed != null)
                {
                    await _onAuthRefreshed(_auth, _logger);
                }
            }

            return _auth?.AplosAccessToken;
        }
    }
}
