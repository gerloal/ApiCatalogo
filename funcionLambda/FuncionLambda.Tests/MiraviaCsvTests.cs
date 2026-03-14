using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FuncionLambda.Models;
using FuncionLambda.Services;
using Xunit;

namespace FuncionLambda.Tests
{
    /// <summary>
    /// Tests de la generación de CSV y el batching en MiraviaServices.
    /// Usa reflexión para acceder a los métodos privados de generación de CSV.
    /// </summary>
    public class MiraviaCsvTests
    {
        // ── Batching ─────────────────────────────────────────────────

        [Theory]
        [InlineData(0,  0)]
        [InlineData(1,  1)]
        [InlineData(20, 1)]
        [InlineData(21, 2)]
        [InlineData(45, 3)]
        [InlineData(60, 3)]
        public void BatchCount_IsCorrectForItemCount(int itemCount, int expectedBatches)
        {
            const int batchSize = 20;
            var items = Enumerable.Range(0, itemCount)
                .Select(i => new ClientItem { Sku = $"SKU{i:000}", Price = 9.99m, Stock = 10 })
                .ToList();

            var batches = items
                .Select((item, idx) => new { item, idx })
                .GroupBy(x => x.idx / batchSize)
                .ToList();

            batches.Count.Should().Be(expectedBatches);
        }

        // ── CSV Headers ───────────────────────────────────────────────

        [Fact]
        public void BuildHeadersCsv_ContainsHeaderRow()
        {
            var csv = InvokeBuildHeadersCsv(new List<MiraviaOrder>());
            csv.Should().Contain("OrderId")
               .And.Contain("CreatedAt")
               .And.Contain("Status")
               .And.Contain("Price");
        }

        [Fact]
        public void BuildHeadersCsv_ContainsOrderData()
        {
            var orders = new List<MiraviaOrder>
            {
                new()
                {
                    OrderId = 123456,
                    CreatedAt = "2025-01-15 10:30:00",
                    Status = "pending",
                    Price = "99.99",
                    Currency = "EUR",
                    AddressInfo = new MiraviaAddressInfo
                    {
                        FirstName = "Juan",
                        LastName = "García",
                        City = "Madrid",
                        Country = "ES"
                    }
                }
            };

            var csv = InvokeBuildHeadersCsv(orders);

            csv.Should().Contain("123456");
            csv.Should().Contain("2025-01-15 10:30:00");
            csv.Should().Contain("pending");
            csv.Should().Contain("99.99");
            csv.Should().Contain("Madrid");
        }

        [Fact]
        public void BuildHeadersCsv_EscapesCommasInFields()
        {
            var orders = new List<MiraviaOrder>
            {
                new()
                {
                    OrderId = 1,
                    AddressInfo = new MiraviaAddressInfo
                    {
                        FirstName = "Ana, María",  // coma en el nombre
                        City = "Alcalá de Henares"
                    }
                }
            };

            var csv = InvokeBuildHeadersCsv(orders);

            // Los campos con comas deben ir entre comillas
            csv.Should().Contain("\"Ana, María\"");
        }

        // ── CSV Lines ─────────────────────────────────────────────────

        [Fact]
        public void BuildLinesCsv_ContainsHeaderRow()
        {
            var csv = InvokeBuildLinesCsv(new List<MiraviaOrderItem>());
            csv.Should().Contain("OrderId")
               .And.Contain("Sku")
               .And.Contain("Quantity")
               .And.Contain("PaidPrice");
        }

        [Fact]
        public void BuildLinesCsv_ContainsItemData()
        {
            var items = new List<MiraviaOrderItem>
            {
                new()
                {
                    OrderId = 123456,
                    OrderItemId = 789,
                    Sku = "SKU-001",
                    Name = "Zapatillas Running",
                    Quantity = 2,
                    PaidPrice = "59.99",
                    Currency = "EUR"
                }
            };

            var csv = InvokeBuildLinesCsv(items);

            csv.Should().Contain("123456");
            csv.Should().Contain("SKU-001");
            csv.Should().Contain("Zapatillas Running");
            csv.Should().Contain("2");
            csv.Should().Contain("59.99");
        }

        [Fact]
        public void BuildLinesCsv_EscapesQuotesInProductNames()
        {
            var items = new List<MiraviaOrderItem>
            {
                new()
                {
                    OrderId = 1,
                    Name = "Camiseta \"Pro\" Series"
                }
            };

            var csv = InvokeBuildLinesCsv(items);

            // Las comillas dobles dentro de campos CSV deben escaparse como ""
            csv.Should().Contain("\"Camiseta \"\"Pro\"\" Series\"");
        }

        // ── Helpers por reflexión ─────────────────────────────────────

        private static string InvokeBuildHeadersCsv(List<MiraviaOrder> orders)
        {
            var method = typeof(MiraviaServices).GetMethod("BuildHeadersCsv",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Should().NotBeNull("BuildHeadersCsv debe existir en MiraviaServices");
            return (string)method!.Invoke(null, new object[] { orders })!;
        }

        private static string InvokeBuildLinesCsv(List<MiraviaOrderItem> items)
        {
            var method = typeof(MiraviaServices).GetMethod("BuildLinesCsv",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method.Should().NotBeNull("BuildLinesCsv debe existir en MiraviaServices");
            return (string)method!.Invoke(null, new object[] { items })!;
        }
    }
}
