using Aplos.Api.Client.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
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

namespace Aplos.Api.Client.Tests
{
    public class AplosPayablesPagingTests
    {
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory = new();
        private readonly Mock<IAccessTokenDecryptor> _mockAccessTokenDecryptor = new();
        private readonly Mock<ILogger<AplosApiClientFactory>> _mockLogger = new();

        [Fact]
        public async Task GetPayables_FollowsEveryPage_WhenAplosTruncatesTheList()
        {
            var handler = new SequencedHttpMessageHandler();
            handler.Enqueue("/auth/clientid", File.ReadAllText("Samples/Response/GET_auth.json"));
            handler.Enqueue("/payables/", PayablesPage(["9001", "9002"], next: "/api/v1/payables/?page_size=50&page_num=2"));
            handler.Enqueue("/payables/", PayablesPage(["9003", "9004"], next: "/api/v1/payables/?page_size=50&page_num=3"));
            handler.Enqueue("/payables/", PayablesPage(["9005"], next: null));

            var payables = await NewClient(handler).GetPayables(new DateOnly(2026, 1, 15));

            Assert.Equal(new[] { "9001", "9002", "9003", "9004", "9005" }, payables.Select(p => p.Id));
            Assert.Equal(3, handler.RequestUris.Count(uri => uri.AbsolutePath == "/payables/"));
        }

        [Fact]
        public async Task GetPayables_SendsTheRangeStartThenRequestsEachNextLinkInTurn()
        {
            var handler = new SequencedHttpMessageHandler();
            handler.Enqueue("/auth/clientid", File.ReadAllText("Samples/Response/GET_auth.json"));
            handler.Enqueue("/payables/", PayablesPage(["9001"], next: "/api/v1/payables/?f_rangestart=2026-01-15&page_size=50&page_num=2"));
            handler.Enqueue("/payables/", PayablesPage(["9002"], next: "/api/v1/payables/?f_rangestart=2026-01-15&page_size=50&page_num=3"));
            handler.Enqueue("/payables/", PayablesPage(["9003"], next: null));

            await NewClient(handler).GetPayables(new DateOnly(2026, 1, 15));

            var queries = handler.RequestUris.Where(uri => uri.AbsolutePath == "/payables/").Select(uri => uri.Query).ToList();
            Assert.Equal(3, queries.Count);
            Assert.Equal("?f_rangestart=2026-01-15", queries[0]);
            Assert.Contains("page_num=2", queries[1]);
            Assert.Contains("page_num=3", queries[2]);
            Assert.All(queries, query => Assert.Contains("f_rangestart=2026-01-15", query));
        }

        [Fact]
        public async Task GetPayables_StopsAtTheFirstPage_WhenThereIsNoNextLink()
        {
            var handler = new SequencedHttpMessageHandler();
            handler.Enqueue("/auth/clientid", File.ReadAllText("Samples/Response/GET_auth.json"));
            handler.Enqueue("/payables/", PayablesPage(["9001"], next: null));

            var payables = await NewClient(handler).GetPayables(new DateOnly(2026, 1, 15));

            Assert.Single(payables);
            Assert.Equal(1, handler.RequestUris.Count(uri => uri.AbsolutePath == "/payables/"));
        }

        private AplosApiClient NewClient(HttpMessageHandler handler)
        {
            // A real IHttpClientFactory hands back a fresh HttpClient per call; reusing one here would trip
            // HttpClient's "BaseAddress after first request" guard as soon as a second page is fetched.
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

        private static string PayablesPage(string[] ids, string next)
        {
            var payables = string.Join(",", ids.Select(id =>
                "{\"id\":\"" + id + "\",\"bill_date\":\"2026-01-15\",\"due_date\":\"2026-02-15\",\"reference_num\":\"INV-" + id + "\",\"amount\":100.00,\"paid\":0}"));

            var links = next == null
                ? "{\"self\":\"/api/v1/payables/\"}"
                : "{\"self\":\"/api/v1/payables/\",\"next\":\"" + next + "\"}";

            return "{\"version\":\"v2_0_1\",\"status\":200,\"links\":" + links + ",\"data\":{\"payables\":[" + payables + "]}}";
        }

        private sealed class SequencedHttpMessageHandler : HttpMessageHandler
        {
            private readonly Dictionary<string, Queue<string>> _responses = [];

            public List<Uri> RequestUris { get; } = [];

            public void Enqueue(string absolutePath, string responseBody)
            {
                if (!_responses.TryGetValue(absolutePath, out var queue))
                {
                    queue = new Queue<string>();
                    _responses[absolutePath] = queue;
                }
                queue.Enqueue(responseBody);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestUris.Add(request.RequestUri);

                if (!_responses.TryGetValue(request.RequestUri.AbsolutePath, out var queue) || queue.Count == 0)
                {
                    throw new InvalidOperationException($"No queued response for '{request.Method} {request.RequestUri}'.");
                }

                // The sample auth token is already expired, so the client re-authenticates before each page;
                // keep the last queued response for a path so auth can be replayed.
                var body = queue.Count == 1 ? queue.Peek() : queue.Dequeue();

                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
