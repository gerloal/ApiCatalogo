using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using FluentAssertions;
using FuncionLambda;
using FuncionLambda.Models;
using FuncionLambda.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace FuncionLambda.Tests
{
    public class PcComponentesServicesTests
    {
        private const string BaseUrl = "https://pccomponentes.mirakl.net";

        // ── CSV de catálogo (OF01) ────────────────────────────────────

        [Fact]
        public void BuildOffersCsv_ContainsHeaderRow()
        {
            var csv = InvokeBuildOffersCsv(new List<ClientItem>());
            csv.Should().Contain("sku")
               .And.Contain("price")
               .And.Contain("quantity");
        }

        [Fact]
        public void BuildOffersCsv_UsesSemicolonSeparator()
        {
            var items = new List<ClientItem>
            {
                new() { Sku = "SKU001", Price = 19.99m, Stock = 10 }
            };
            var csv = InvokeBuildOffersCsv(items);
            // La línea de datos debe contener separadores semicolón
            csv.Should().Contain("SKU001;");
        }

        [Fact]
        public void BuildOffersCsv_IncludesItemData()
        {
            var items = new List<ClientItem>
            {
                new() { Sku = "SKU001", Price = 19.99m, Stock = 10 },
                new() { Sku = "SKU002", Price = 5.50m, Stock = 3 }
            };
            var csv = InvokeBuildOffersCsv(items);

            csv.Should().Contain("SKU001");
            csv.Should().Contain("19.99");
            csv.Should().Contain("10");
            csv.Should().Contain("SKU002");
            csv.Should().Contain("5.50");
        }

        [Fact]
        public void BuildOffersCsv_FormatsPriceWithTwoDecimals()
        {
            var items = new List<ClientItem> { new() { Sku = "X", Price = 100m, Stock = 1 } };
            var csv = InvokeBuildOffersCsv(items);
            csv.Should().Contain("100.00");
        }

        [Fact]
        public void BuildOffersCsv_UsesDotAsDecimalSeparator()
        {
            var items = new List<ClientItem> { new() { Sku = "X", Price = 9.99m, Stock = 1 } };
            var csv = InvokeBuildOffersCsv(items);
            // Formato invariante: punto como separador decimal
            csv.Should().Contain("9.99");
            csv.Should().NotContain("9,99");
        }

        [Fact]
        public void BuildOffersCsv_HandlesNullStock()
        {
            var items = new List<ClientItem> { new() { Sku = "X", Price = 10m, Stock = null } };
            var csv = InvokeBuildOffersCsv(items);
            // Debe existir la línea aunque stock sea null (columna vacía)
            csv.Should().Contain("X").And.Contain("10.00");
        }

        // ── CSV de cabeceras de pedidos ───────────────────────────────

        [Fact]
        public void BuildHeadersCsv_ContainsRequiredColumns()
        {
            var csv = InvokeBuildHeadersCsv(new List<MiraklOrder>());
            csv.Should().Contain("OrderId")
               .And.Contain("OrderState")
               .And.Contain("TotalPrice")
               .And.Contain("Currency");
        }

        [Fact]
        public void BuildHeadersCsv_ContainsOrderData()
        {
            var orders = new List<MiraklOrder>
            {
                new()
                {
                    OrderId         = "ORD-123",
                    OrderState      = "SHIPPING",
                    TotalPrice      = 49.99m,
                    CurrencyIsoCode = "EUR",
                    Customer = new MiraklCustomer
                    {
                        Firstname = "Juan",
                        Lastname  = "García",
                        Email     = "juan@test.com",
                        BillingAddress  = new MiraklAddress { City = "Madrid",   CountryIsoCode = "ES" },
                        ShippingAddress = new MiraklAddress { City = "Valencia", CountryIsoCode = "ES" }
                    }
                }
            };

            var csv = InvokeBuildHeadersCsv(orders);

            csv.Should().Contain("ORD-123");
            csv.Should().Contain("SHIPPING");
            csv.Should().Contain("49.99");
            csv.Should().Contain("EUR");
            csv.Should().Contain("Madrid");
        }

        [Fact]
        public void BuildHeadersCsv_EscapesCommasInCustomerName()
        {
            var orders = new List<MiraklOrder>
            {
                new()
                {
                    OrderId = "X",
                    Customer = new MiraklCustomer { Firstname = "Ana, María", Lastname = "López" }
                }
            };

            var csv = InvokeBuildHeadersCsv(orders);
            // El nombre con coma debe estar entre comillas en el CSV
            csv.Should().Contain("\"Ana, María López\"");
        }

        // ── CSV de líneas de pedido ───────────────────────────────────

        [Fact]
        public void BuildLinesCsv_ContainsRequiredColumns()
        {
            var csv = InvokeBuildLinesCsv(new List<MiraklOrderLine>());
            csv.Should().Contain("OrderId")
               .And.Contain("OrderLineId")
               .And.Contain("OfferSku")
               .And.Contain("Quantity");
        }

        [Fact]
        public void BuildLinesCsv_ContainsParentOrderIdAndLineData()
        {
            var lines = new List<MiraklOrderLine>
            {
                new()
                {
                    ParentOrderId   = "ORD-123",
                    OrderLineId     = "ORD-123-1",
                    OfferSku        = "SKU001",
                    ProductTitle    = "Producto Test",
                    Quantity        = 2,
                    Price           = 24.99m,
                    CurrencyIsoCode = "EUR",
                    OrderLineState  = "SHIPPING"
                }
            };

            var csv = InvokeBuildLinesCsv(lines);

            csv.Should().Contain("ORD-123");
            csv.Should().Contain("ORD-123-1");
            csv.Should().Contain("SKU001");
            csv.Should().Contain("2");
            csv.Should().Contain("24.99");
        }

        // ── Polling de import (UpdateCatalogAsync) ────────────────────

        [Fact]
        public async Task UpdateCatalogAsync_ReturnsComplete_WhenImportSucceeds()
        {
            const string importJson = "{\"import_id\":42}";
            const string statusJson = @"{
                ""import_id"": 42, ""status"": ""COMPLETE"",
                ""lines_read"": 5, ""lines_in_success"": 5, ""lines_in_error"": 0,
                ""offer_inserted"": 3, ""offer_updated"": 2, ""offer_deleted"": 0,
                ""has_error_report"": false, ""mode"": ""NORMAL""
            }";

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
                    req.Method == HttpMethod.Post
                        ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(importJson) }
                        : new HttpResponseMessage(HttpStatusCode.OK)      { Content = new StringContent(statusJson) });

            var apiClient = new PcComponentesApiClient("key", BaseUrl, new HttpClient(handler.Object));
            var service   = new PcComponentesServices(apiClient, new MockLambdaContext());

            var items = new List<ClientItem>
            {
                new() { Sku = "SKU001", Price = 10m, Stock = 5 },
                new() { Sku = "SKU002", Price = 20m, Stock = 3 }
            };

            var result = await service.UpdateCatalogAsync(items);

            result.ImportId.Should().Be(42);
            result.ImportStatus.Should().Be("COMPLETE");
            result.OffersInserted.Should().Be(3);
            result.OffersUpdated.Should().Be(2);
            result.LinesInError.Should().Be(0);
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateCatalogAsync_AddsError_WhenImportFails()
        {
            const string importJson = "{\"import_id\":99}";
            const string statusJson = @"{
                ""import_id"": 99, ""status"": ""FAILED"",
                ""lines_read"": 3, ""lines_in_success"": 0, ""lines_in_error"": 3,
                ""offer_inserted"": 0, ""offer_updated"": 0, ""offer_deleted"": 0,
                ""has_error_report"": true, ""mode"": ""NORMAL""
            }";

            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
                    req.Method == HttpMethod.Post
                        ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(importJson) }
                        : new HttpResponseMessage(HttpStatusCode.OK)      { Content = new StringContent(statusJson) });

            var apiClient = new PcComponentesApiClient("key", BaseUrl, new HttpClient(handler.Object));
            var service   = new PcComponentesServices(apiClient, new MockLambdaContext());

            var result = await service.UpdateCatalogAsync(
                new List<ClientItem> { new() { Sku = "X", Price = 1m, Stock = 1 } });

            result.ImportStatus.Should().Be("FAILED");
            result.HasErrorReport.Should().BeTrue();
            result.Errors.Should().NotBeEmpty();
        }

        [Fact]
        public void PcComponentesFeedResult_ToSummary_IncludesKeyFields()
        {
            var result = new PcComponentesFeedResult
            {
                ImportId      = 123,
                ImportStatus  = "COMPLETE",
                OffersInserted = 5,
                OffersUpdated  = 3,
                LinesInError   = 0
            };

            var summary = result.ToSummary();

            summary.Should().Contain("123");
            summary.Should().Contain("COMPLETE");
            summary.Should().Contain("5");
            summary.Should().Contain("3");
        }

        // ── Helpers de reflexión ──────────────────────────────────────

        private static string InvokeBuildOffersCsv(List<ClientItem> items)
        {
            var method = typeof(PcComponentesServices).GetMethod("BuildOffersCsv",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (string)method!.Invoke(null, new object[] { items })!;
        }

        private static string InvokeBuildHeadersCsv(List<MiraklOrder> orders)
        {
            var method = typeof(PcComponentesServices).GetMethod("BuildHeadersCsv",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (string)method!.Invoke(null, new object[] { orders })!;
        }

        private static string InvokeBuildLinesCsv(List<MiraklOrderLine> lines)
        {
            var method = typeof(PcComponentesServices).GetMethod("BuildLinesCsv",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (string)method!.Invoke(null, new object[] { lines })!;
        }
    }

    // ─── Helpers de test ────────────────────────────────────────────────

    internal class MockLambdaContext : ILambdaContext
    {
        public string          AwsRequestId         => "test-request-id";
        public IClientContext  ClientContext         => null!;
        public string          FunctionName         => "test-function";
        public string          FunctionVersion      => "$LATEST";
        public ICognitoIdentity Identity             => null!;
        public string          InvokedFunctionArn   => "arn:aws:lambda:eu-west-1:123456789:function:test";
        public ILambdaLogger   Logger               => new MockLambdaLogger();
        public string          LogGroupName         => "/aws/lambda/test";
        public string          LogStreamName        => "test-stream";
        public int             MemoryLimitInMB      => 512;
        public TimeSpan        RemainingTime        => TimeSpan.FromMinutes(5);
    }

    internal class MockLambdaLogger : ILambdaLogger
    {
        public void Log(string message)     { }
        public void LogLine(string message) { }
    }
}
