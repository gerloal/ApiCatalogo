using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using FuncionLambda;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FuncionLambda.Tests
{
    public class CatalogoServiceTests
    {
        private readonly Mock<IAmazonS3>       _s3Mock  = new();
        private readonly Mock<IAmazonDynamoDB> _ddbMock = new();
        private readonly ILambdaContext        _ctx     = new MockLambdaContext();

        private void SetupS3WithContent(string csvContent)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            _s3Mock.Setup(s => s.GetObjectAsync(
                            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new GetObjectResponse { ResponseStream = stream });
        }

        // ── TransformCatalogFromToListItems ───────────────────────────

        [Fact]
        public async Task TransformCatalog_ParsesValidLines()
        {
            SetupS3WithContent("SKU001;10;9.99\nSKU002;5;19.50");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().HaveCount(2);
            items[0].Sku.Should().Be("SKU001");
            items[0].Stock.Should().Be(10);
            items[0].Price.Should().Be(9.99m);
            items[1].Sku.Should().Be("SKU002");
            items[1].Price.Should().Be(19.50m);
        }

        [Fact]
        public async Task TransformCatalog_IgnoresLinesTooShort()
        {
            SetupS3WithContent("SKU001;10;9.99\nMALFORMED\nSKU002;5;5.00");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().HaveCount(2);
        }

        [Fact]
        public async Task TransformCatalog_HandlesInvalidPrice_DefaultsToZero()
        {
            SetupS3WithContent("SKU001;10;NOT_A_PRICE");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().HaveCount(1);
            items[0].Price.Should().Be(0m);
        }

        [Fact]
        public async Task TransformCatalog_HandlesInvalidStock_SetsNull()
        {
            SetupS3WithContent("SKU001;NOT_A_NUMBER;9.99");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().HaveCount(1);
            items[0].Stock.Should().BeNull();
        }

        [Fact]
        public async Task TransformCatalog_TrimsWhitespaceFromSku()
        {
            SetupS3WithContent("  SKU001  ;10;9.99");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items[0].Sku.Should().Be("SKU001");
        }

        [Fact]
        public async Task TransformCatalog_ParsesPriceWithDotDecimalSeparator()
        {
            SetupS3WithContent("SKU001;1;1234.56");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items[0].Price.Should().Be(1234.56m);
        }

        [Fact]
        public async Task TransformCatalog_ReturnsEmptyList_WhenFileIsEmpty()
        {
            SetupS3WithContent("");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().BeEmpty();
        }

        [Fact]
        public async Task TransformCatalog_ReturnsEmptyList_WhenS3Throws()
        {
            _s3Mock.Setup(s => s.GetObjectAsync(
                            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new Exception("S3 unavailable"));

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().BeEmpty();
        }

        [Fact]
        public async Task TransformCatalog_IgnoresOnlyHeaderLines_WithLessThan3Parts()
        {
            // La primera línea sólo tiene 2 partes → debe ignorarse
            SetupS3WithContent("Header;Row\nSKU001;5;9.99");

            var items = await CatalogoService.TransformCatalogFromToListItems(
                _s3Mock.Object, "bucket", "key", _ctx);

            items.Should().HaveCount(1);
        }

        // ── UpdateJobAsync ────────────────────────────────────────────

        [Fact]
        public async Task UpdateJobAsync_CallsDynamoDBUpdateOnce()
        {
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new UpdateItemResponse());

            await CatalogoService.UpdateJobAsync(_ddbMock.Object, "test-table", "job-001", "DONE", "OK");

            _ddbMock.Verify(d => d.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateJobAsync_SilentlyHandlesDynamoDBException()
        {
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new Exception("DynamoDB error"));

            // No debe propagar la excepción
            await CatalogoService.UpdateJobAsync(
                _ddbMock.Object, "test-table", "job-001", "FAILED", "error message");
        }

        [Fact]
        public async Task UpdateJobAsync_TruncatesMessageAt500Chars()
        {
            UpdateItemRequest captured = null;
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<UpdateItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new UpdateItemResponse());

            var longMessage = new string('X', 1000);
            await CatalogoService.UpdateJobAsync(_ddbMock.Object, "test-table", "job-001", "FAILED", longMessage);

            captured.ExpressionAttributeValues[":m"].S.Length.Should().BeLessThanOrEqualTo(500);
        }

        [Fact]
        public async Task UpdateJobAsync_SetsCorrectStatusValue()
        {
            UpdateItemRequest captured = null;
            _ddbMock.Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
                    .Callback<UpdateItemRequest, CancellationToken>((req, _) => captured = req)
                    .ReturnsAsync(new UpdateItemResponse());

            await CatalogoService.UpdateJobAsync(_ddbMock.Object, "test-table", "job-001", "DONE", "ok");

            captured.ExpressionAttributeValues[":s"].S.Should().Be("DONE");
        }

        // ── ExtractJobId (vía reflexión, método privado) ──────────────

        private static string CallExtractJobId(string body)
        {
            var method = typeof(CatalogoService).GetMethod(
                "ExtractJobId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (string)method!.Invoke(null, new object[] { body })!;
        }

        [Fact]
        public void ExtractJobId_ReturnsJobId_WhenValidJson()
        {
            var result = CallExtractJobId("{\"jobId\":\"job-abc\",\"tenantId\":\"t1\"}");
            result.Should().Be("job-abc");
        }

        [Fact]
        public void ExtractJobId_ReturnsUnknown_WhenInvalidJson()
        {
            var result = CallExtractJobId("not-json");
            result.Should().Be("unknown");
        }

        [Fact]
        public void ExtractJobId_ReturnsUnknown_WhenJobIdFieldMissing()
        {
            var result = CallExtractJobId("{\"tenantId\":\"t1\"}");
            result.Should().Be("unknown");
        }

        [Fact]
        public void ExtractJobId_ReturnsUnknown_WhenBodyIsEmpty()
        {
            var result = CallExtractJobId("");
            result.Should().Be("unknown");
        }
    }
}
