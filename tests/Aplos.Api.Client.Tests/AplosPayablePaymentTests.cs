using Aplos.Api.Client.Abstractions;
using Aplos.Api.Client.Exceptions;
using Aplos.Api.Client.Models;
using Aplos.Api.Client.Models.Response;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Aplos.Api.Client.Tests;

public class AplosPayablePaymentTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory = new();
    private readonly Mock<IAccessTokenDecryptor> _mockAccessTokenDecryptor = new();
    private readonly Mock<ILogger<AplosApiClientFactory>> _mockLogger = new();

    [Fact]
    public async Task GetPayable_ReadsTheLivePayable_AndAsksForAllTags()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Respond("/auth/clientid", HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json"));
        handler.Respond("/payables/9001", HttpStatusCode.OK,
            "{\"version\":\"v2_0_1\",\"status\":200,\"data\":{\"payable\":{\"id\":\"9001\",\"reference_num\":\"INV-9001\",\"amount\":125.50,\"paid\":25.50}}}");

        var response = await NewClient(handler).GetPayable("9001");

        Assert.Equal("9001", response.Data.Payable.Id);
        Assert.Equal(125.50m, response.Data.Payable.Amount);
        Assert.Equal(25.50m, response.Data.Payable.PaidAmount);

        // allTags is undocumented and Aplos silently drops every tag without it.
        var uri = Assert.Single(handler.RequestUris, u => u.AbsolutePath == "/payables/9001");
        Assert.Contains("allTags=y", uri.Query);
    }

    [Fact]
    public async Task PayPayable_PutsOnlyThePaidDateAndCashAccount()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Respond("/auth/clientid", HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json"));
        handler.Respond("/payables/9001/pay", HttpStatusCode.OK, PaidPayableResponse());

        var response = await NewClient(handler).PayPayable("9001", new AplosApiPayablePaymentModel
        {
            PaidDate = new DateOnly(2026, 9, 10),
            CashAccountNumber = 1000m
        });

        Assert.Equal("9001", response.Data.Payable.Id);
        Assert.Equal(response.Data.Payable.Amount, response.Data.Payable.PaidAmount);

        var payRequest = handler.Requests.Single(request => request.uri.AbsolutePath == "/payables/9001/pay");
        Assert.Equal(HttpMethod.Put, payRequest.method);

        var json = JObject.Parse(payRequest.body);
        Assert.Equal("2026-09-10", json["paid_date"].Value<string>());
        Assert.Equal(1000m, json["cash_account"]["account_number"].Value<decimal>());
        Assert.Equal(["paid_date", "cash_account"], json.Properties().Select(property => property.Name));
    }

    // Body Aplos returned on 2026-09-17 for paying an already-paid payable (spike-out/20260917_161852698_PUT_..._4262797_pay.json).
    [Fact]
    public async Task PayPayable_OnAnAlreadyPaidPayable_Surfaces405()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Respond("/auth/clientid", HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json"));
        handler.Respond("/payables/9001/pay", HttpStatusCode.MethodNotAllowed,
            "{\"version\":\"v2_0_1\",\"status\":405,\"exception\":{\"message\":\"Caller requested a resource that is not available.\",\"code\":3001}}");

        var client = NewClient(handler);
        Func<Task<AplosApiPayableResponse>> pay = () => client.PayPayable("9001", new AplosApiPayablePaymentModel { CashAccountNumber = 1000m });

        var exception = await Assert.ThrowsAsync<AplosApiException>(pay);
        Assert.Equal((int)HttpStatusCode.MethodNotAllowed, exception.AplosApiError.Status);
        Assert.Equal(3001, exception.AplosApiError.Exception.Code);
    }

    [Fact]
    public async Task PayPayable_ReturnsError_WithHttp422Response()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Respond("/auth/clientid", HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json"));
        handler.Respond("/payables/9001/pay", HttpStatusCode.UnprocessableEntity,
            JsonConvert.SerializeObject(new AplosApiErrorResponse { Status = (int)HttpStatusCode.UnprocessableEntity }));

        var client = NewClient(handler);
        Func<Task<AplosApiPayableResponse>> pay = () => client.PayPayable("9001", new AplosApiPayablePaymentModel { CashAccountNumber = 1000m });

        var exception = await Assert.ThrowsAsync<AplosApiException>(pay);
        Assert.Equal((int)HttpStatusCode.UnprocessableEntity, exception.AplosApiError.Status);
    }

    // Shape of a payable read on 2026-10-14 (S13, spike-out/s13-20261014); term is a field the typed model lacks.
    private const string RawPayable =
        "{\"id\":4323771,\"bill_date\":\"2026-10-14\",\"modified\":\"2026-10-14T02:50:06.808-0700\",\"reference_num\":\"PEXBP-S13-A\","
      + "\"note\":\"S13 probe\",\"amount\":1.01,\"term\":15,\"contact\":{\"id\":16181656},"
      + "\"lines\":[{\"id\":19427579,\"amount\":0.51,\"fund\":{\"id\":249047},\"tags\":[{\"id\":120728},{\"id\":184294}]},"
      + "{\"id\":19427581,\"amount\":0.50,\"fund\":{\"id\":225755},\"tags\":[{\"id\":201040}]}]}";

    [Fact]
    public async Task GetPayableForUpdate_ReturnsThePayableAsAplosWroteIt_AndAsksForAllTags()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Respond("/auth/clientid", HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json"));
        handler.Respond("/payables/4323771", HttpStatusCode.OK, $"{{\"version\":\"v2_0_1\",\"status\":200,\"data\":{{\"payable\":{RawPayable}}}}}");

        var payable = await NewClient(handler).GetPayableForUpdate("4323771");

        Assert.True(JToken.DeepEquals(ParseRaw(RawPayable), payable));
        Assert.Equal(JTokenType.String, payable["modified"].Type);
        Assert.Equal("2026-10-14T02:50:06.808-0700", payable["modified"].Value<string>());

        var uri = Assert.Single(handler.RequestUris, u => u.AbsolutePath == "/payables/4323771");
        Assert.Contains("allTags=y", uri.Query);
    }

    [Fact]
    public async Task UpdatePayable_PutsThePayableBackUnchanged_ExceptAmount()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Respond("/auth/clientid", HttpStatusCode.OK, File.ReadAllText("Samples/Response/GET_auth.json"));
        handler.Respond("/payables/4323771", HttpStatusCode.OK, $"{{\"version\":\"v2_0_1\",\"status\":200,\"data\":{{\"payable\":{RawPayable}}}}}");
        var client = NewClient(handler);

        var payable = await client.GetPayableForUpdate("4323771");
        payable["note"] = "S13 probe | [PEXBP:4323771] | Paid via ACH";
        await client.UpdatePayable("4323771", payable);

        var put = handler.Requests.Single(request => request.method == HttpMethod.Put);
        Assert.Equal("/payables/4323771", put.uri.AbsolutePath);

        var expected = ParseRaw(RawPayable);
        expected.Remove("amount");
        expected["note"] = "S13 probe | [PEXBP:4323771] | Paid via ACH";
        var sent = ParseRaw(put.body);
        Assert.True(JToken.DeepEquals(expected, sent), put.body);
        Assert.Contains("\"modified\":\"2026-10-14T02:50:06.808-0700\"", put.body);
        Assert.Contains("\"amount\":0.51", put.body);

        // The caller's copy keeps its amount.
        Assert.Equal(1.01m, payable["amount"].Value<decimal>());
    }

    private AplosApiClient NewClient(HttpMessageHandler handler)
    {
        _mockHttpClientFactory.Setup(factory => factory.CreateClient("")).Returns(() => new HttpClient(handler, disposeHandler: false));

        return new AplosApiClient(
            "acctid",
            "clientid",
            "pk",
            new Uri("https://www.pexcard.com/"),
            _mockHttpClientFactory.Object,
            _mockAccessTokenDecryptor.Object,
            _mockLogger.Object,
            null,
            null);
    }

    private static JObject ParseRaw(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader);
    }

    private static string PaidPayableResponse()
        => "{\"version\":\"v2_0_1\",\"message\":\"paid: 9001\",\"status\":200,"
         + "\"data\":{\"payable\":{\"id\":\"9001\",\"amount\":125.50,\"paid\":125.50}}}";

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode statusCode, string body)> _responses = [];

        public List<(HttpMethod method, Uri uri, string body)> Requests { get; } = [];

        public IEnumerable<Uri> RequestUris => Requests.Select(request => request.uri);

        public void Respond(string absolutePath, HttpStatusCode statusCode, string responseBody)
            => _responses[absolutePath] = (statusCode, responseBody);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri, requestBody));

            if (!_responses.TryGetValue(request.RequestUri.AbsolutePath, out var response))
            {
                throw new InvalidOperationException($"No response configured for '{request.Method} {request.RequestUri}'.");
            }

            return new HttpResponseMessage
            {
                StatusCode = response.statusCode,
                Content = new StringContent(response.body, Encoding.UTF8, "application/json")
            };
        }
    }
}
