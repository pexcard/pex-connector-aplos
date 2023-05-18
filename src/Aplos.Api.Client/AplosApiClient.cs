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
using System.Threading;

namespace Aplos.Api.Client
{
    public class AplosApiClient : IAplosApiClient
    {
        private const string APLOS_ENDPOINT_AUTH = "auth/";
        private const string APLOS_ENDPOINT_ACCOUNTS = "accounts/";
        private const string APLOS_ENDPOINT_CONTACTS = "contacts/";
        private const string APLOS_ENDPOINT_FUNDS = "funds/";
        private const string APLOS_ENDPOINT_TRANSACTIONS = "transactions/";
        private const string APLOS_ENDPOINT_PARTNERS = "partners/";
        private const string APLOS_ENDPOINT_PARTNERS_VERIFY = APLOS_ENDPOINT_PARTNERS + "verify";
        private const string APLOS_ENDPOINT_TAGS = "tags/";
        private const string APLOS_ENDPOINT_TAX_TAGS = "taxtags/";
        private const string APLOS_ENDPOINT_PAYABLES = "payables/";

        public const string APLOS_ACCOUNT_CATEGORY_ASSET = "asset";
        public const string APLOS_ACCOUNT_CATEGORY_EXPENSE = "expense";
        public const string APLOS_ACCOUNT_CATEGORY_LIABILITY = "liability";
        private static readonly ImmutableHashSet<string> _validAplosAccountCategories = new HashSet<string>
        {
            APLOS_ACCOUNT_CATEGORY_ASSET,
            APLOS_ACCOUNT_CATEGORY_EXPENSE,
            APLOS_ACCOUNT_CATEGORY_LIABILITY
        }.ToImmutableHashSet();

        private readonly string _aplosAccountId;
        private readonly string _aplosClientId;
        private readonly string _aplosPrivateKey;
        private readonly Uri _aplosApiEndpointUri;
        private readonly IHttpClientFactory _clientFactory;
        private readonly IAccessTokenDecryptor _accessTokenDecryptor;
        private readonly ILogger _logger;
        private readonly Func<ILogger, AplosAuthModel> _onAuthInitializing;
        private readonly Func<AplosAuthModel, ILogger, CancellationToken, Task> _onAuthRefreshed;

        public AplosApiClient(
            string aplosAccountId,
            string aplosClientId,
            string aplosPrivateKey,
            Uri aplosApiEndpointUri,
            IHttpClientFactory clientFactory,
            IAccessTokenDecryptor accessTokenDecryptor,
            ILogger logger,
            Func<ILogger, AplosAuthModel> onAuthInitializing = null,
            Func<AplosAuthModel, ILogger, CancellationToken, Task> onAuthRefreshed = null)
        {
            _aplosAccountId = aplosAccountId;
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

        private async Task<HttpClient> MakeAuthenticatedAplosHttpClient(bool includeAplosAccountId)
        {
            var httpClient = MakeAplosHttpClient();

            string aplosAccessToken = await GetAplosAccessToken();

            //Aplos uses a nonstandard Authorization header; we have to purposely add it without validation or we'll get an exception.
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer: {aplosAccessToken}");

            if (includeAplosAccountId && !string.IsNullOrWhiteSpace(_aplosAccountId))
            {
                httpClient.DefaultRequestHeaders.Add("aplos-account-id", _aplosAccountId);
            }

            return httpClient;
        }

        private async Task<TResponseContent> InvokeAplosApi<TResponseContent>(
            HttpClient httpClient,
            HttpContent httpRequestContent,
            HttpMethod httpMethod,
            string endpoint,
            CancellationToken cancellationToken)
        {
            var httpRequest = new HttpRequestMessage(httpMethod, endpoint);

            if (httpRequestContent != null)
            {
                httpRequest.Content = httpRequestContent;
            }

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogDebug(responseBody);

            var errorResponse = JsonConvert.DeserializeObject<AplosApiErrorResponse>(responseBody);
            if (!response.IsSuccessStatusCode || errorResponse?.Exception != null)
            {
                var aplosApiException = new AplosApiException(errorResponse);

                var requestContent = await httpRequestContent?.ReadAsStringAsync();

                _logger.LogError(aplosApiException, $"Error invoking Aplos API.\nRequestBody:\n{requestContent}\nResponseBody:\n{responseBody}");
                throw aplosApiException;
            }

            return JsonConvert.DeserializeObject<TResponseContent>(responseBody);
        }

        private async Task<TResponseContent> InvokeAplosApi<TResponseContent>(
            HttpMethod httpMethod,
            string endpoint,
            CancellationToken cancellationToken)
        {
            return await InvokeAplosApi<TResponseContent>(
                MakeAplosHttpClient(),
                null,
                httpMethod,
                endpoint,
                cancellationToken);
        }

        private async Task<TResponseContent> InvokeAplosApiWithAccessToken<TRequestContent, TResponseContent>(
            HttpMethod httpMethod,
            string endpoint,
            TRequestContent requestContent,
            bool includeAplosAccountId = true,
            CancellationToken cancellationToken = default)
        {
            string content = JsonConvert.SerializeObject(requestContent);

            var httpRequestContent = new StringContent(
                content,
                Encoding.UTF8,
                "application/json");

            return await InvokeAplosApi<TResponseContent>(
                await MakeAuthenticatedAplosHttpClient(includeAplosAccountId),
                httpRequestContent,
                httpMethod,
                endpoint,
                cancellationToken);
        }

        private async Task<TResponseContent> InvokeAplosApiWithAccessToken<TResponseContent>(
            HttpMethod httpMethod,
            string endpoint,
            bool includeAplosAccountId = true,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApi<TResponseContent>(
                await MakeAuthenticatedAplosHttpClient(includeAplosAccountId),
                null,
                httpMethod,
                endpoint,
                cancellationToken);
        }

        private async Task<string> DecryptEncryptedAplosAccessToken(
            string aplosPrivateKey,
            string encryptedAplosAccessToken)
        {
            return await Task.Run(() => _accessTokenDecryptor.Decrypt(aplosPrivateKey, encryptedAplosAccessToken));
        }

        public async Task<bool> GetAndValidateAplosAccessToken(CancellationToken cancellationToken = default)
        {
            bool isValid = true;
            try
            {
                //Aplos doesn't have a clean way to verify that they will actually accept an access token that you have.
                //To get around this, we are validating by calling /accounts, but this could be replaced with calling any safe endpoint.
                await GetAplosAccessToken(cancellationToken);
                await GetAccounts(cancellationToken: cancellationToken);
            }
            catch (AplosApiException ex)
            {
                _logger.LogWarning(ex, $"{nameof(GetAndValidateAplosAccessToken)}: Error getting access token");
                isValid = false;
            }

            return isValid;
        }

        public async Task<AplosApiAuthResponse> GetAuth(string aplosClientId, CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApi<AplosApiAuthResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_AUTH + aplosClientId,
                cancellationToken);
        }

        public async Task<List<AplosApiAccountDetail>> GetAccounts(
            string aplosExpenseCategory = null,
            CancellationToken cancellationToken = default)
        {
            //We only want enabled accounts.
            var endpoint = APLOS_ENDPOINT_ACCOUNTS + "?f_enabled=y";
            if (_validAplosAccountCategories.Contains(aplosExpenseCategory))
            {
                endpoint += $"&f_type={aplosExpenseCategory}";
            }

            var response = await InvokeAplosApiWithAccessToken<AplosApiAccountListResponse>(
                HttpMethod.Get,
                endpoint,
                cancellationToken: cancellationToken);

            var result = new List<AplosApiAccountDetail>(response.Data.Accounts);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiAccountListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""),
                    cancellationToken: cancellationToken);

                result.AddRange(response.Data.Accounts);
            }
            return result;
        }

        public async Task<AplosApiAccountListResponse> GetAccounts(
            int pageSize,
            int pageNum,
            string aplosExpenseCategory = null,
            CancellationToken cancellationToken = default)
        {
            //We only want enabled accounts.
            var endpoint = $"{APLOS_ENDPOINT_ACCOUNTS}?f_enabled=y&page_size={pageSize}&page_num={pageNum}";
            if (_validAplosAccountCategories.Contains(aplosExpenseCategory))
            {
                endpoint += $"&f_type={aplosExpenseCategory}";
            }

            return await InvokeAplosApiWithAccessToken<AplosApiAccountListResponse>(
                HttpMethod.Get,
                endpoint,
                cancellationToken: cancellationToken);
        }

        public async Task<AplosApiAccountResponse> GetAccount(
            decimal aplosAccountNumber,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiAccountResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_ACCOUNTS + aplosAccountNumber,
                cancellationToken: cancellationToken);
        }

        public async Task<List<AplosApiFundDetail>> GetFunds(CancellationToken cancellationToken = default)
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiFundListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_FUNDS,
                cancellationToken: cancellationToken);

            var result = new List<AplosApiFundDetail>(response.Data.Funds);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiFundListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""),
                    cancellationToken: cancellationToken);

                result.AddRange(response.Data.Funds);
            }
            return result;
        }

        public async Task<AplosApiFundListResponse> GetFunds(
            int pageSize,
            int pageNum,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiFundListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_FUNDS}?page_size={pageSize}&page_num={pageNum}",
                cancellationToken: cancellationToken);
        }

        public async Task<AplosApiFundResponse> GetFund(
            int aplosFundId,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiFundResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_FUNDS + aplosFundId,
                cancellationToken: cancellationToken);
        }

        public async Task<List<AplosApiContactDetail>> GetContacts(CancellationToken cancellationToken = default)
        {
            //We will only need to sync transactions with businesses, not individuals. Assuming our client has their Aplos contacts set up correctly.
            var response = await InvokeAplosApiWithAccessToken<AplosApiContactListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_CONTACTS + "?f_type=company",
                cancellationToken: cancellationToken);

            var result = new List<AplosApiContactDetail>(response.Data.Contacts);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiContactListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""),
                    cancellationToken: cancellationToken);

                result.AddRange(response.Data.Contacts);
            }
            return result;
        }

        public async Task<AplosApiContactListResponse> GetContacts(int pageSize,
            int pageNum,
            CancellationToken cancellationToken = default)
        {
            //We will only need to sync transactions with businesses, not individuals. Assuming our client has their Aplos contacts set up correctly.
            return await InvokeAplosApiWithAccessToken<AplosApiContactListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_CONTACTS}?f_type=company&page_size={pageSize}&page_num={pageNum}",
                cancellationToken: cancellationToken);
        }

        public async Task<AplosApiContactResponse> GetContact(
            int aplosContactId,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiContactResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_CONTACTS + aplosContactId,
                cancellationToken: cancellationToken);
        }

        public async Task<List<AplosApiTagCategoryDetail>> GetTags(CancellationToken cancellationToken = default)
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiTagListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_TAGS,
                cancellationToken: cancellationToken);

            var rawTagCategories = new List<AplosApiTagCategoryDetail>(response.Data.TagCategories);

            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiTagListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""),
                    cancellationToken: cancellationToken);

                rawTagCategories.AddRange(response.Data.TagCategories);
            }

            //Handle when Aplos sends the same tag category more than once.
            //This happens when there are enough tags to spill over into a new page. The tag category is repeated from a previous page, but the tag groups and tags within those groups are unique per page.
            var result = new List<AplosApiTagCategoryDetail>();
            foreach (var tagCategory in rawTagCategories)
            {
                var existingCategory = result.Find(r => r.Id == tagCategory.Id);
                if (existingCategory == null)
                {
                    result.Add(tagCategory);
                }
                else
                {
                    existingCategory.TagGroups.AddRange(tagCategory.TagGroups);
                }
            }

            return result;
        }

        public async Task<AplosApiTagListResponse> GetTags(
            int pageSize,
            int pageNum,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTagListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TAGS}?page_size={pageSize}&page_num={pageNum}",
                cancellationToken: cancellationToken);
        }

        public async Task<List<AplosApiTaxTagCategoryDetail>> GetTaxTags(CancellationToken cancellationToken = default)
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiTaxTagListResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_TAX_TAGS,
                cancellationToken: cancellationToken);

            var rawTagCategories = new List<AplosApiTaxTagCategoryDetail>(response.Data.TagCategories);

            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiTaxTagListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""),
                    cancellationToken: cancellationToken);

                rawTagCategories.AddRange(response.Data.TagCategories);
            }

            //Handle when Aplos sends the same tag category more than once.
            //This happens when there are enough tags to spill over into a new page. The tag category is repeated from a previous page, but the tag groups and tags within those groups are unique per page.
            var result = new List<AplosApiTaxTagCategoryDetail>();
            foreach (var tagCategory in rawTagCategories)
            {
                var existingCategory = result.Find(r => r.Id == tagCategory.Id);
                if (existingCategory == null)
                {
                    result.Add(tagCategory);
                }
                else
                {
                    existingCategory.TaxTags.AddRange(tagCategory.TaxTags);
                }
            }

            return result;
        }

        public async Task<AplosApiTaxTagListResponse> GetTaxTags(
            int pageSize,
            int pageNum,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTaxTagListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TAX_TAGS}?page_size={pageSize}&page_num={pageNum}",
                cancellationToken: cancellationToken);
        }

        public async Task<List<AplosApiTransactionDetail>> GetTransactions(
            DateTime startDate,
            CancellationToken cancellationToken = default)
        {
            var response = await InvokeAplosApiWithAccessToken<AplosApiTransactionListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TRANSACTIONS}?f_rangestart={startDate:yyyy-MM-dd}",
                cancellationToken: cancellationToken);

            var result = new List<AplosApiTransactionDetail>(response.Data.Transactions);
            while (!string.IsNullOrEmpty(response.Links?.Next))
            {
                response = await InvokeAplosApiWithAccessToken<AplosApiTransactionListResponse>(
                    HttpMethod.Get,
                    response.Links.Next.Replace("/api/v1/", ""),
                    cancellationToken: cancellationToken);

                result.AddRange(response.Data.Transactions);
            }
            return result;
        }

        public async Task<AplosApiTransactionListResponse> GetTransactions(
            DateTime startDate,
            int pageSize,
            int pageNum,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTransactionListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_TRANSACTIONS}?page_size={pageSize}&page_num={pageNum}&f_rangestart={startDate:yyyy-MM-dd}",
                cancellationToken: cancellationToken);
        }

        public async Task<AplosApiTransactionResponse> GetTransaction(
            int aplosTransactionId, CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTransactionResponse>(
                HttpMethod.Get,
                APLOS_ENDPOINT_TRANSACTIONS + aplosTransactionId,
                cancellationToken: cancellationToken);
        }

        public async Task<AplosApiTransactionResponse> CreateTransaction(
            AplosApiTransactionDetail aplosTransaction,
            CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiTransactionDetail, AplosApiTransactionResponse>(
                HttpMethod.Post,
                APLOS_ENDPOINT_TRANSACTIONS,
                aplosTransaction,
                cancellationToken: cancellationToken);
        }

        public async Task<AplosApiPartnerVerificationResponse> GetPartnerVerification(CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiPartnerVerificationResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_PARTNERS_VERIFY}?aplos-account-id={_aplosAccountId}",
                cancellationToken: cancellationToken);
        }

        public Task<bool> IsHealthy(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        private AplosAuthModel _auth;
        public async Task<string> GetAplosAccessToken(CancellationToken cancellationToken = default)
        {
            if (_auth == null && _onAuthInitializing != null)
            {
                _auth = _onAuthInitializing(_logger);
            }

            if (_auth?.AplosAccessTokenExpiresAt <= DateTime.UtcNow)
            {
                _auth = null;
                _logger.LogInformation($"{nameof(_auth.AplosAccessToken)} expired");
            }

            if (_auth == null)
            {
                AplosApiAuthResponse authResponse = await GetAuth(_aplosClientId, cancellationToken);
                var decryptedAplosAccessToken = await DecryptEncryptedAplosAccessToken(_aplosPrivateKey, authResponse.Data.Token);

                _auth = new AplosAuthModel
                {
                    AplosAccessToken = decryptedAplosAccessToken,
                    AplosAccessTokenExpiresAt = authResponse.Data.Expires
                };

                if (_onAuthRefreshed != null)
                {
                    await _onAuthRefreshed(_auth, _logger, cancellationToken);
                }
            }

            return _auth?.AplosAccessToken;
        }

        public async Task<AplosApiPayablesListResponse> GetPayables(DateTime startDate, CancellationToken cancellationToken = default)
        {
            return await InvokeAplosApiWithAccessToken<AplosApiPayablesListResponse>(
                HttpMethod.Get,
                $"{APLOS_ENDPOINT_PAYABLES}?f_rangestart={startDate:yyyy-MM-dd}",
                cancellationToken: cancellationToken);
        }
    }
}
