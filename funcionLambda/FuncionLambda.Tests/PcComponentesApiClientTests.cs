using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FuncionLambda;
using FuncionLambda.Models;
using Moq;
using Moq.Protected;
using Xunit;

namespace FuncionLambda.Tests
{
    public class PcComponentesApiClientTests
    {
        private const string ApiKey     = "test_api_key_12345";
        private const string BaseUrl    = "https://pccomponentes.mirakl.net";
        private const string SandboxUrl = "https://sandbox.pccomponentes.mirakl.net";

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

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
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

            var secret = new PcComponentesSecret
            {
                ApiKey = ApiKey, BaseUrl = BaseUrl, SandboxBaseUrl = SandboxUrl
            };
            var client = new PcComponentesApiClient(secret, useSandbox: false, new HttpClient(handler.Object));
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

            var secret = new PcComponentesSecret
            {
                ApiKey = ApiKey, BaseUrl = BaseUrl, SandboxBaseUrl = SandboxUrl
            };
            var client = new PcComponentesApiClient(secret, useSandbox: true, new HttpClient(handler.Object));
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

            var secret = new PcComponentesSecret
            {
                ApiKey = ApiKey, BaseUrl = BaseUrl, SandboxBaseUrl = ""
            };
            var client = new PcComponentesApiClient(secret, useSandbox: true, new HttpClient(handler.Object));
            await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

            capturedUrl.Should().StartWith(BaseUrl);
        }

        // ── ImportOffersAsync ─────────────────────────────────────────

        [Fact]
        public async Task ImportOffersAsync_SendsMultipartFormData()
        {
            HttpRequestMessage? capturedRequest = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"import_id\":12345,\"product_import_id\":null}")
                },
                req => capturedRequest = req);

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var csv    = "sku;price;quantity\nSKU001;19.99;10";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

            var result = await client.ImportOffersAsync(stream);

            result.Should().NotBeNull();
            result!.ImportId.Should().Be(12345);
            capturedRequest!.Method.Should().Be(HttpMethod.Post);
            capturedRequest.RequestUri!.PathAndQuery.Should().Be("/api/offers/imports");
            capturedRequest.Content.Should().BeOfType<MultipartFormDataContent>();
        }

        [Fact]
        public async Task ImportOffersAsync_IncludesImportModeField()
        {
            string? capturedBody = null;

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                {
                    capturedBody = await req.Content!.ReadAsStringAsync();
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"import_id\":1}")
                });

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("sku;price;quantity"));
            await client.ImportOffersAsync(stream, importMode: "REPLACE");

            capturedBody.Should().Contain("REPLACE");
        }

        // ── GetImportStatusAsync ──────────────────────────────────────

        [Fact]
        public async Task GetImportStatusAsync_HitsCorrectEndpointAndDeserializes()
        {
            string? capturedPath = null;
            const string statusJson = @"{
                ""import_id"": 99,
                ""status"": ""COMPLETE"",
                ""lines_in_success"": 10,
                ""lines_in_error"": 0,
                ""lines_in_pending"": 0,
                ""lines_read"": 10,
                ""has_error_report"": false,
                ""offer_inserted"": 5,
                ""offer_updated"": 5,
                ""offer_deleted"": 0,
                ""mode"": ""NORMAL""
            }";

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(statusJson) },
                req => capturedPath = req.RequestUri?.PathAndQuery);

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var status = await client.GetImportStatusAsync(99L);

            capturedPath.Should().Be("/api/offers/imports/99");
            status.Should().NotBeNull();
            status!.Status.Should().Be("COMPLETE");
            status.LinesInSuccess.Should().Be(10);
            status.OfferInserted.Should().Be(5);
        }

        // ── GetOrdersAsync ────────────────────────────────────────────

        [Fact]
        public async Task GetOrdersAsync_EncodesDateRangeAndPaginationInUrl()
        {
            string? capturedQuery = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"orders\":[], \"total_count\":0}")
                },
                req => capturedQuery = req.RequestUri?.Query);

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            await client.GetOrdersAsync(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc),
                offset: 50, max: 25);

            capturedQuery.Should().Contain("start_date=");
            capturedQuery.Should().Contain("end_date=");
            capturedQuery.Should().Contain("max=25");
            capturedQuery.Should().Contain("offset=50");
        }

        [Fact]
        public async Task GetOrdersAsync_DeserializesOrdersResponse()
        {
            const string ordersJson = @"{
                ""orders"": [
                    {
                        ""order_id"": ""ORD-001"",
                        ""order_state"": ""SHIPPING"",
                        ""total_price"": 49.99,
                        ""currency_iso_code"": ""EUR"",
                        ""order_lines"": []
                    }
                ],
                ""total_count"": 1
            }";

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ordersJson) });

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var result = await client.GetOrdersAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

            result.Should().NotBeNull();
            result!.TotalCount.Should().Be(1);
            result.Orders.Should().HaveCount(1);
            result.Orders[0].OrderId.Should().Be("ORD-001");
            result.Orders[0].TotalPrice.Should().Be(49.99m);
        }

        // ── StartAsyncOrderExportAsync ────────────────────────────────

        [Fact]
        public async Task StartAsyncOrderExportAsync_PostsToCorrectEndpointAndDeserializes()
        {
            string? capturedPath = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"tracking_id\":\"track-xyz-123\"}")
                },
                req => capturedPath = req.RequestUri?.PathAndQuery);

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var result = await client.StartAsyncOrderExportAsync(
                DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

            capturedPath.Should().Be("/api/orders/async-export");
            result.Should().NotBeNull();
            result!.TrackingId.Should().Be("track-xyz-123");
        }

        // ── GetAsyncExportStatusAsync ─────────────────────────────────

        [Fact]
        public async Task GetAsyncExportStatusAsync_HitsCorrectEndpointAndDeserializes()
        {
            string? capturedPath = null;
            const string statusJson =
                @"{""status"":""COMPLETE"",""download_url"":""https://example.com/export.csv""}";

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(statusJson) },
                req => capturedPath = req.RequestUri?.PathAndQuery);

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            var result = await client.GetAsyncExportStatusAsync("track-xyz");

            capturedPath.Should().Be("/api/orders/async-export/status/track-xyz");
            result.Should().NotBeNull();
            result!.Status.Should().Be("COMPLETE");
            result.DownloadUrl.Should().Be("https://example.com/export.csv");
        }

        [Fact]
        public async Task GetAsyncExportStatusAsync_EscapesTrackingIdInUrl()
        {
            string? capturedPath = null;

            var handler = CreateMockHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"RUNNING\",\"download_url\":\"\"}")
                },
                req => capturedPath = req.RequestUri?.PathAndQuery);

            var client = new PcComponentesApiClient(ApiKey, BaseUrl, new HttpClient(handler.Object));
            await client.GetAsyncExportStatusAsync("track/special id");

            // Los caracteres especiales deben estar URL-encoded
            capturedPath.Should().NotContain("track/special id");
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
