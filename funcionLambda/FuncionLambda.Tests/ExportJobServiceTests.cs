using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using FuncionLambda.Models;
using FuncionLambda.Services;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FuncionLambda.Tests
{
    public class ExportJobServiceTests
    {
        private readonly Mock<IAmazonDynamoDB> _ddbMock;
        private readonly Mock<IAmazonSQS>      _sqsMock;
        private readonly ExportJobService      _sut;

        public ExportJobServiceTests()
        {
            _ddbMock = new Mock<IAmazonDynamoDB>();
            _sqsMock = new Mock<IAmazonSQS>();

            _ddbMock.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PutItemResponse());
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new UpdateItemResponse());
            _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SendMessageResponse { MessageId = "msg-001" });

            _sut = new ExportJobService(_ddbMock.Object, _sqsMock.Object, "test-table", "https://sqs.test/queue");
        }

        private static ExportOrdersRequest ValidRequest() => new()
        {
            TenantId  = "tenant-1",
            StartDate = new DateTime(2026, 1, 1,  0, 0, 0, DateTimeKind.Utc),
            EndDate   = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Format    = "CSV"
        };

        // ── CreateExportJobAsync ──────────────────────────────────────

        [Fact]
        public async Task CreateExportJobAsync_ReturnsNonEmptyGuidJobId()
        {
            var jobId = await _sut.CreateExportJobAsync(ValidRequest());

            jobId.Should().NotBeNullOrEmpty();
            Guid.TryParse(jobId, out _).Should().BeTrue();
        }

        [Fact]
        public async Task CreateExportJobAsync_CallsPutItemOnce()
        {
            await _sut.CreateExportJobAsync(ValidRequest());

            _ddbMock.Verify(d => d.PutItemAsync(
                It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateExportJobAsync_UsesCorrectDynamoDbKeyFormat()
        {
            PutItemRequest captured = null;
            _ddbMock.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<PutItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new PutItemResponse());

            await _sut.CreateExportJobAsync(ValidRequest());

            captured.Item["pk"].S.Should().Be("TENANT#tenant-1");
            captured.Item["sk"].S.Should().StartWith("JOB#");
        }

        [Fact]
        public async Task CreateExportJobAsync_SetsStatusToPending()
        {
            PutItemRequest captured = null;
            _ddbMock.Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<PutItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new PutItemResponse());

            await _sut.CreateExportJobAsync(ValidRequest());

            captured.Item["status"].S.Should().Be("PENDING");
        }

        [Fact]
        public async Task CreateExportJobAsync_SendsExactlyOneMessageToSQS()
        {
            await _sut.CreateExportJobAsync(ValidRequest());

            _sqsMock.Verify(s => s.SendMessageAsync(
                It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateExportJobAsync_SqsMessageBodyContainsTenantIdAndOperation()
        {
            SendMessageRequest capturedMsg = null;
            _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<SendMessageRequest, CancellationToken>((req, _) => capturedMsg = req)
                    .ReturnsAsync(new SendMessageResponse());

            await _sut.CreateExportJobAsync(ValidRequest());

            capturedMsg.MessageBody.Should().Contain("tenant-1");
            capturedMsg.MessageBody.Should().Contain("EXPORT_ORDERS");
        }

        // ── GetJobStatusAsync ─────────────────────────────────────────

        [Fact]
        public async Task GetJobStatusAsync_ReturnsNull_WhenItemNotFound()
        {
            _ddbMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

            var result = await _sut.GetJobStatusAsync("tenant-1", "job-001");

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetJobStatusAsync_MapsAllFieldsCorrectly()
        {
            _ddbMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GetItemResponse
                    {
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["jobId"]       = new AttributeValue { S = "job-001" },
                            ["tenantId"]    = new AttributeValue { S = "tenant-1" },
                            ["status"]      = new AttributeValue { S = "DONE" },
                            ["totalOrders"] = new AttributeValue { N = "5" },
                            ["totalLines"]  = new AttributeValue { N = "12" },
                            ["headersUrl"]  = new AttributeValue { S = "https://s3.test/headers.csv" },
                            ["linesUrl"]    = new AttributeValue { S = "https://s3.test/lines.csv" },
                            ["createdAt"]   = new AttributeValue { S = "2026-01-01T00:00:00Z" },
                            ["updatedAt"]   = new AttributeValue { S = "2026-01-01T01:00:00Z" }
                        }
                    });

            var result = await _sut.GetJobStatusAsync("tenant-1", "job-001");

            result.Should().NotBeNull();
            result.JobId.Should().Be("job-001");
            result.TenantId.Should().Be("tenant-1");
            result.Status.Should().Be("DONE");
            result.TotalOrders.Should().Be(5);
            result.TotalLines.Should().Be(12);
            result.HeadersPresignedUrl.Should().Be("https://s3.test/headers.csv");
            result.LinesPresignedUrl.Should().Be("https://s3.test/lines.csv");
        }

        [Fact]
        public async Task GetJobStatusAsync_UsesCompositeKeyInQuery()
        {
            GetItemRequest captured = null;
            _ddbMock.Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<GetItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

            await _sut.GetJobStatusAsync("tenant-1", "job-001");

            captured.Key["pk"].S.Should().Be("TENANT#tenant-1");
            captured.Key["sk"].S.Should().Be("JOB#job-001");
        }

        // ── UpdateJobStatusToRunningAsync ─────────────────────────────

        [Fact]
        public async Task UpdateJobStatusToRunningAsync_CallsUpdateItemOnce()
        {
            await _sut.UpdateJobStatusToRunningAsync("tenant-1", "job-001");

            _ddbMock.Verify(d => d.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateJobStatusToRunningAsync_SetsStatusToRunning()
        {
            UpdateItemRequest captured = null;
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<UpdateItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new UpdateItemResponse());

            await _sut.UpdateJobStatusToRunningAsync("tenant-1", "job-001");

            captured.ExpressionAttributeValues[":status"].S.Should().Be("RUNNING");
        }

        [Fact]
        public async Task UpdateJobStatusToRunningAsync_UsesCorrectKey()
        {
            UpdateItemRequest captured = null;
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<UpdateItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new UpdateItemResponse());

            await _sut.UpdateJobStatusToRunningAsync("tenant-1", "job-001");

            captured.Key["pk"].S.Should().Be("TENANT#tenant-1");
            captured.Key["sk"].S.Should().Be("JOB#job-001");
        }
    }
}
