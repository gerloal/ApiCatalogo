using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using FuncionLambda.Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FuncionLambda.Tests
{
    public class LambdaServicesTests
    {
        private readonly Mock<IAmazonDynamoDB> _ddbMock;
        private readonly Mock<IAmazonSQS>      _sqsMock;
        private readonly LambdaServices        _sut;
        private readonly ILambdaContext        _ctx;

        public LambdaServicesTests()
        {
            _ddbMock = new Mock<IAmazonDynamoDB>();
            _sqsMock = new Mock<IAmazonSQS>();

            _ddbMock.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PutItemResponse());
            _ddbMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });
            _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SendMessageResponse { MessageId = "msg-test" });

            _sut = new LambdaServices(_ddbMock.Object, _sqsMock.Object, "test-table", "https://sqs.test/queue");
            _ctx = new MockLambdaContext();
        }

        private static APIGatewayProxyRequest MakeRequest(
            string method,
            string path,
            string body = null,
            Dictionary<string, string> headers = null,
            Dictionary<string, string> queryParams = null) => new()
        {
            HttpMethod            = method,
            Path                  = path,
            Body                  = body,
            Headers               = headers ?? new Dictionary<string, string>(),
            QueryStringParameters = queryParams
        };

        private static string ValidBody() => JsonSerializer.Serialize(new
        {
            tenantId  = "tenant-1",
            startDate = "2026-01-01T00:00:00Z",
            endDate   = "2026-01-15T00:00:00Z",
            format    = "CSV"
        });

        // ── Routing ──────────────────────────────────────────────────

        [Fact]
        public async Task FunctionHandler_Returns404_ForUnknownPath()
        {
            var resp = await _sut.FunctionHandler(MakeRequest("GET", "/unknown/path"), _ctx);
            resp.StatusCode.Should().Be(404);
        }

        [Fact]
        public async Task FunctionHandler_Returns404_ForPutMethod()
        {
            var resp = await _sut.FunctionHandler(MakeRequest("PUT", "/exports/orders"), _ctx);
            resp.StatusCode.Should().Be(404);
        }

        [Fact]
        public async Task FunctionHandler_Returns404_ForDeleteMethod()
        {
            var resp = await _sut.FunctionHandler(MakeRequest("DELETE", "/exports/orders"), _ctx);
            resp.StatusCode.Should().Be(404);
        }

        // ── POST /exports/orders — validación ────────────────────────

        [Fact]
        public async Task HandleCreateExport_Returns400_WithMalformedJson()
        {
            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: "not-valid-json"), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleCreateExport_Returns400_WhenTenantIdMissing()
        {
            var body = JsonSerializer.Serialize(new
            {
                startDate = "2026-01-01T00:00:00Z",
                endDate   = "2026-01-15T00:00:00Z"
            });

            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: body), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleCreateExport_Returns400_WhenStartDateAfterEndDate()
        {
            var body = JsonSerializer.Serialize(new
            {
                tenantId  = "tenant-1",
                startDate = "2026-01-15T00:00:00Z",
                endDate   = "2026-01-01T00:00:00Z"
            });

            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: body), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleCreateExport_Returns400_WhenRangeExceeds30Days()
        {
            var body = JsonSerializer.Serialize(new
            {
                tenantId  = "tenant-1",
                startDate = "2026-01-01T00:00:00Z",
                endDate   = "2026-02-10T00:00:00Z"   // 40 días
            });

            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: body), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleCreateExport_Returns400_WhenFormatIsNotCsv()
        {
            var body = JsonSerializer.Serialize(new
            {
                tenantId  = "tenant-1",
                startDate = "2026-01-01T00:00:00Z",
                endDate   = "2026-01-15T00:00:00Z",
                format    = "JSON"
            });

            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: body), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleCreateExport_Returns400_WhenBodyIsNull()
        {
            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: null), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        // ── POST /exports/orders — happy path ────────────────────────

        [Fact]
        public async Task HandleCreateExport_Returns202_WithValidRequest()
        {
            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: ValidBody()), _ctx);

            resp.StatusCode.Should().Be(202);
        }

        [Fact]
        public async Task HandleCreateExport_ResponseBodyContainsJobIdAndPendingStatus()
        {
            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: ValidBody()), _ctx);

            var parsed = JsonSerializer.Deserialize<ExportOrdersResponse>(
                resp.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            parsed.JobId.Should().NotBeNullOrEmpty();
            Guid.TryParse(parsed.JobId, out _).Should().BeTrue();
            parsed.Status.Should().Be("PENDING");
        }

        // ── GET /exports/orders/{jobId} — validación ─────────────────

        [Fact]
        public async Task HandleGetJobStatus_Returns400_WhenJobIdMissingFromPath()
        {
            var req = MakeRequest("GET", "/exports/orders/",
                headers: new Dictionary<string, string> { ["X-Tenant-Id"] = "tenant-1" });

            var resp = await _sut.FunctionHandler(req, _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleGetJobStatus_Returns400_WhenTenantIdMissing()
        {
            // Sin header ni query param de tenantId
            var resp = await _sut.FunctionHandler(
                MakeRequest("GET", "/exports/orders/job-001"), _ctx);

            resp.StatusCode.Should().Be(400);
        }

        [Fact]
        public async Task HandleGetJobStatus_Returns404_WhenJobNotFound()
        {
            // DDB ya configurado en setUp para devolver dict vacío
            var req = MakeRequest("GET", "/exports/orders/job-001",
                headers: new Dictionary<string, string> { ["X-Tenant-Id"] = "tenant-1" });

            var resp = await _sut.FunctionHandler(req, _ctx);

            resp.StatusCode.Should().Be(404);
        }

        [Fact]
        public async Task HandleGetJobStatus_Returns200_WithExistingJob()
        {
            _ddbMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GetItemResponse
                    {
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["jobId"]       = new AttributeValue { S = "job-001" },
                            ["tenantId"]    = new AttributeValue { S = "tenant-1" },
                            ["status"]      = new AttributeValue { S = "DONE" },
                            ["totalOrders"] = new AttributeValue { N = "3" },
                            ["totalLines"]  = new AttributeValue { N = "8" }
                        }
                    });

            var req = MakeRequest("GET", "/exports/orders/job-001",
                headers: new Dictionary<string, string> { ["X-Tenant-Id"] = "tenant-1" });

            var resp = await _sut.FunctionHandler(req, _ctx);

            resp.StatusCode.Should().Be(200);
            resp.Body.Should().Contain("DONE");
        }

        [Fact]
        public async Task HandleGetJobStatus_AcceptsTenantFromQueryParam()
        {
            _ddbMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GetItemResponse
                    {
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["jobId"]       = new AttributeValue { S = "job-001" },
                            ["tenantId"]    = new AttributeValue { S = "tenant-1" },
                            ["status"]      = new AttributeValue { S = "RUNNING" },
                            ["totalOrders"] = new AttributeValue { N = "0" },
                            ["totalLines"]  = new AttributeValue { N = "0" }
                        }
                    });

            var req = MakeRequest("GET", "/exports/orders/job-001",
                queryParams: new Dictionary<string, string> { ["tenantId"] = "tenant-1" });

            var resp = await _sut.FunctionHandler(req, _ctx);

            resp.StatusCode.Should().Be(200);
        }

        // ── Auth ─────────────────────────────────────────────────────

        [Fact]
        public async Task HandleCreateExport_Returns401_WhenApiKeyIsWrong()
        {
            Environment.SetEnvironmentVariable("API_KEY", "valid-key");
            try
            {
                var req = MakeRequest("POST", "/exports/orders", body: ValidBody(),
                    headers: new Dictionary<string, string> { ["X-Api-Key"] = "wrong-key" });

                var resp = await _sut.FunctionHandler(req, _ctx);

                resp.StatusCode.Should().Be(401);
            }
            finally
            {
                Environment.SetEnvironmentVariable("API_KEY", null);
            }
        }

        [Fact]
        public async Task HandleCreateExport_Returns202_WhenNoApiKeyConfigured()
        {
            Environment.SetEnvironmentVariable("API_KEY", null);

            var resp = await _sut.FunctionHandler(
                MakeRequest("POST", "/exports/orders", body: ValidBody()), _ctx);

            resp.StatusCode.Should().Be(202);
        }
    }
}
