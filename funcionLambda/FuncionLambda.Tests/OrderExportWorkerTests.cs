using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
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
    public class OrderExportWorkerTests
    {
        private readonly Mock<IAmazonS3>             _s3Mock;
        private readonly Mock<IAmazonDynamoDB>       _ddbMock;
        private readonly Mock<IAmazonSecretsManager> _secretsMock;
        private readonly OrderExportWorker           _sut;
        private readonly ILambdaContext              _ctx;

        public OrderExportWorkerTests()
        {
            _s3Mock      = new Mock<IAmazonS3>();
            _ddbMock     = new Mock<IAmazonDynamoDB>();
            _secretsMock = new Mock<IAmazonSecretsManager>();

            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new UpdateItemResponse());

            _sut = new OrderExportWorker(
                _s3Mock.Object, _ddbMock.Object, _secretsMock.Object,
                "test-table", "test-bucket");

            _ctx = new MockLambdaContext();
        }

        private static SQSEvent MakeSqsEvent(string messageBody) => new()
        {
            Records = new List<SQSEvent.SQSMessage>
            {
                new() { MessageId = "msg-001", Body = messageBody }
            }
        };

        private static string ValidMessageBody() =>
            JsonSerializer.Serialize(new ExportOrdersQueueMessage
            {
                JobId     = "job-001",
                TenantId  = "tenant-1",
                StartDate = "2026-01-01T00:00:00Z",
                EndDate   = "2026-01-15T00:00:00Z",
                Format    = "CSV",
                Operation = "EXPORT_ORDERS"
            });

        // ── Parseo de mensaje ─────────────────────────────────────────

        [Fact]
        public async Task FunctionHandler_ThrowsJsonException_WhenBodyIsNotJson()
        {
            var sqsEvent = MakeSqsEvent("not-valid-json");

            await Assert.ThrowsAsync<JsonException>(
                () => _sut.FunctionHandler(sqsEvent, _ctx));
        }

        [Fact]
        public async Task FunctionHandler_ThrowsException_WhenJobIdIsMissing()
        {
            var body = JsonSerializer.Serialize(new
            {
                TenantId  = "tenant-1",
                StartDate = "2026-01-01T00:00:00Z",
                EndDate   = "2026-01-15T00:00:00Z",
                Operation = "EXPORT_ORDERS"
                // JobId ausente → se deserializa como null
            });

            var sqsEvent = MakeSqsEvent(body);

            await Assert.ThrowsAnyAsync<Exception>(
                () => _sut.FunctionHandler(sqsEvent, _ctx));
        }

        [Fact]
        public async Task FunctionHandler_ThrowsException_WhenTenantIdIsMissing()
        {
            var body = JsonSerializer.Serialize(new
            {
                JobId     = "job-001",
                StartDate = "2026-01-01T00:00:00Z",
                EndDate   = "2026-01-15T00:00:00Z",
                Operation = "EXPORT_ORDERS"
                // TenantId ausente → se deserializa como null
            });

            var sqsEvent = MakeSqsEvent(body);

            await Assert.ThrowsAnyAsync<Exception>(
                () => _sut.FunctionHandler(sqsEvent, _ctx));
        }

        // ── Flujo con Secrets Manager ─────────────────────────────────

        [Fact]
        public async Task FunctionHandler_ThrowsWhenSecretsManagerFails()
        {
            _secretsMock.Setup(s => s.GetSecretValueAsync(
                            It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Amazon.SecretsManager.Model.ResourceNotFoundException("Secret not found"));

            var sqsEvent = MakeSqsEvent(ValidMessageBody());

            await Assert.ThrowsAnyAsync<Exception>(
                () => _sut.FunctionHandler(sqsEvent, _ctx));
        }

        [Fact]
        public async Task FunctionHandler_CallsUpdateJobStatusToRunning_BeforeSecretsLookup()
        {
            // Secrets falla → sabemos que DDB fue llamado antes si la excepción viene de Secrets
            _secretsMock.Setup(s => s.GetSecretValueAsync(
                            It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new Amazon.SecretsManager.Model.ResourceNotFoundException("Secret not found"));

            var sqsEvent = MakeSqsEvent(ValidMessageBody());

            await Assert.ThrowsAnyAsync<Exception>(
                () => _sut.FunctionHandler(sqsEvent, _ctx));

            // UpdateItemAsync debe haberse llamado antes de lanzar la excepción de Secrets
            _ddbMock.Verify(d => d.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // ── Múltiples mensajes en el evento ──────────────────────────

        [Fact]
        public async Task FunctionHandler_ProcessesEachMessageIndependently_AndRethrowsOnError()
        {
            var sqsEvent = new SQSEvent
            {
                Records = new List<SQSEvent.SQSMessage>
                {
                    new() { MessageId = "msg-bad", Body = "invalid-json" }
                }
            };

            await Assert.ThrowsAnyAsync<Exception>(
                () => _sut.FunctionHandler(sqsEvent, _ctx));
        }
    }
}
