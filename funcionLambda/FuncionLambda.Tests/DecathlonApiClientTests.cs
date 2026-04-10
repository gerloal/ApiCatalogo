using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FuncionLambda;
using Moq;
using Moq.Protected;
using Xunit;

namespace FuncionLambda.Tests
{
    public class DecathlonApiClientTests
    {
        private const string ApiKey     = "test_api_key_decathlon";
        private const string BaseUrl    = "https://decathlon.mirakl.net";
        private const string SandboxUrl = "https://sandbox.decathlon.mirakl.net";

        // ── Autenticación ────────────────────────────────────────────

        [Fact]
        public async Task AllRequests_IncludeAuthorizationHeader()
        {
            string? capturedHeader = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"orders\":[], \"total_count\":0}")
                },
                req =>
                {
                    capturedHeader = req.Headers.TryGetValues("Authorization", out var values)
                        ? string.Join("", values)
                        : null;
                });

            var client = new DecathlonApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            capturedHeader.Should().Be(ApiKey);
        }

        // ── URL Sandbox vs Producción ─────────────────────────────────

        [Fact]
        public async Task Constructor_UsesProductionUrl_WhenUseSandboxFalse()
        {
            string? capturedUrl = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"orders\":[], \"total_count\":0}")
                },
                req => capturedUrl = req.RequestUri?.ToString());

            var secret = new DecathlonSecret
            {
                ApiKey = ApiKey, BaseUrl = BaseUrl, SandboxBaseUrl = SandboxUrl
            };
            var client = new DecathlonApiClient(secret, useSandbox: false, new HttpClient(handler.Object));
            await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            capturedUrl.Should().StartWith(BaseUrl);
        }

        [Fact]
        public async Task Constructor_UsesSandboxUrl_WhenUseSandboxTrue()
        {
            string? capturedUrl = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"orders\":[], \"total_count\":0}")
                },
                req => capturedUrl = req.RequestUri?.ToString());

            var secret = new DecathlonSecret
            {
                ApiKey = ApiKey, BaseUrl = BaseUrl, SandboxBaseUrl = SandboxUrl
            };
            var client = new DecathlonApiClient(secret, useSandbox: true, new HttpClient(handler.Object));
            await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            capturedUrl.Should().StartWith(SandboxUrl);
        }

        [Fact]
        public async Task Constructor_FallsBackToProductionUrl_WhenSandboxUrlIsEmpty()
        {
            string? capturedUrl = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"orders\":[], \"total_count\":0}")
                },
                req => capturedUrl = req.RequestUri?.ToString());

            var secret = new DecathlonSecret
            {
                ApiKey = ApiKey, BaseUrl = BaseUrl, SandboxBaseUrl = ""
            };
            var client = new DecathlonApiClient(secret, useSandbox: true, new HttpClient(handler.Object));
            await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            capturedUrl.Should().StartWith(BaseUrl);
        }

        // ── ImportOffersAsync ─────────────────────────────────────────

        [Fact]
        public async Task ImportOffersAsync_PostsToCorrectEndpoint()
        {
            string? capturedPath = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"import_id\":55555,\"product_import_id\":null}")
                },
                req => capturedPath = req.RequestUri?.PathAndQuery);

            var client = new DecathlonApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("sku;price;quantity\nDEC001;29.99;5"));

            var result = await client.ImportOffersAsync(stream);

            capturedPath.Should().Be("/api/offers/imports");
            result.Should().NotBeNull();
            result!.ImportId.Should().Be(55555);
        }

        [Fact]
        public async Task ImportOffersAsync_SendsMultipartFormData()
        {
            HttpRequestMessage? capturedRequest = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"import_id\":1,\"product_import_id\":null}")
                },
                req => capturedRequest = req);

            var client = new DecathlonApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("sku;price;quantity\nDEC001;29.99;5"));

            await client.ImportOffersAsync(stream);

            capturedRequest!.Method.Should().Be(HttpMethod.Post);
            capturedRequest.Content.Should().BeOfType<MultipartFormDataContent>();
        }

        // ── GetOrdersAsync ────────────────────────────────────────────

        [Fact]
        public async Task GetOrdersAsync_BuildsCorrectDateRangeQuery()
        {
            string? capturedQuery = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"orders\":[], \"total_count\":0}")
                },
                req => capturedQuery = req.RequestUri?.Query);

            var client = new DecathlonApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            await client.GetOrdersAsync(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc),
                offset: 100, max: 50);

            capturedQuery.Should().Contain("start_date=");
            capturedQuery.Should().Contain("end_date=");
            capturedQuery.Should().Contain("max=50");
            capturedQuery.Should().Contain("offset=100");
        }

        [Fact]
        public async Task GetOrdersAsync_DeserializesOrdersResponse()
        {
            const string ordersJson = @"{
                ""orders"": [
                    {
                        ""order_id"": ""DEC-2026-001"",
                        ""order_state"": ""RECEIVED"",
                        ""total_price"": 89.95,
                        ""currency_iso_code"": ""EUR"",
                        ""order_lines"": []
                    }
                ],
                ""total_count"": 1
            }";

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ordersJson) });

            var client = new DecathlonApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var result = await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

            result.Should().NotBeNull();
            result!.TotalCount.Should().Be(1);
            result.Orders.Should().HaveCount(1);
            result.Orders[0].OrderId.Should().Be("DEC-2026-001");
            result.Orders[0].TotalPrice.Should().Be(89.95m);
        }

        // ── GetImportStatusAsync ──────────────────────────────────────

        [Fact]
        public async Task GetImportStatusAsync_HitsCorrectEndpointAndDeserializes()
        {
            string? capturedPath = null;
            const string statusJson = @"{
                ""import_id"": 55555,
                ""status"": ""COMPLETE"",
                ""lines_in_success"": 5,
                ""lines_in_error"": 0,
                ""lines_in_pending"": 0,
                ""lines_read"": 5,
                ""has_error_report"": false,
                ""offer_inserted"": 3,
                ""offer_updated"": 2,
                ""offer_deleted"": 0,
                ""mode"": ""NORMAL""
            }";

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(statusJson) },
                req => capturedPath = req.RequestUri?.PathAndQuery);

            var client = new DecathlonApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var status = await client.GetImportStatusAsync(55555L);

            capturedPath.Should().Be("/api/offers/imports/55555");
            status.Should().NotBeNull();
            status!.Status.Should().Be("COMPLETE");
            status.OfferInserted.Should().Be(3);
            status.OfferUpdated.Should().Be(2);
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static Mock<HttpMessageHandler> CreateMockHandler(
            HttpResponseMessage response,
            Action<HttpRequestMessage>? capture = null)
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capture?.Invoke(req))
                .ReturnsAsync(response);
            return handler;
        }
    }
}
