using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using FuncionLambda;
using FuncionLambda.Models;
using Xunit;

namespace FuncionLambda.Tests
{
    /// <summary>
    /// Tests de integración contra el sandbox de Miravia Open Platform.
    ///
    /// PREREQUISITOS:
    ///   1. Acceder a https://open.miravia.com → App Console → Testing Tools → "Get Token"
    ///   2. Rellenar las variables de entorno o la sección de constantes de esta clase:
    ///        MIRAVIA_SANDBOX_APP_KEY
    ///        MIRAVIA_SANDBOX_APP_SECRET
    ///        MIRAVIA_SANDBOX_ACCESS_TOKEN
    ///   3. Crear al menos 1 pedido de prueba desde App Console → Testing Tools
    ///
    /// EJECUCIÓN SOLO INTEGRATION:
    ///   dotnet test --filter "Category=Integration"
    ///
    /// EJECUCIÓN SIN INTEGRATION (CI normal):
    ///   dotnet test --filter "Category!=Integration"
    /// </summary>
    [Trait("Category", "Integration")]
    public class MiraviaSandboxIntegrationTests
    {
        // ── Credenciales sandbox ──────────────────────────────────────
        // Rellenar aquí o via variables de entorno
        private static readonly string AppKey      = Env("MIRAVIA_SANDBOX_APP_KEY",      "");
        private static readonly string AppSecret   = Env("MIRAVIA_SANDBOX_APP_SECRET",   "");
        private static readonly string AccessToken = Env("MIRAVIA_SANDBOX_ACCESS_TOKEN", "");

        private static string Env(string name, string fallback)
            => Environment.GetEnvironmentVariable(name) ?? fallback;

        private MiraviaApiClient Client => new(AppKey, AppSecret, AccessToken, useSandbox: true);

        // ── Guardia ───────────────────────────────────────────────────

        private void SkipIfNoCredentials()
        {
            if (string.IsNullOrWhiteSpace(AppKey) || string.IsNullOrWhiteSpace(AppSecret) || string.IsNullOrWhiteSpace(AccessToken))
                throw new SkipException("Credenciales sandbox no configuradas. Definir MIRAVIA_SANDBOX_APP_KEY, MIRAVIA_SANDBOX_APP_SECRET y MIRAVIA_SANDBOX_ACCESS_TOKEN.");
        }

        // ── Tests ─────────────────────────────────────────────────────

        [Fact]
        public async Task GetOrders_Sandbox_ReturnsSuccessResponse()
        {
            SkipIfNoCredentials();

            var result = await Client.GetAsync<MiraviaOrdersData>("/orders/get",
                new Dictionary<string, string>
                {
                    ["created_after"]  = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss"),
                    ["created_before"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["limit"]          = "10",
                    ["offset"]         = "0"
                });

            result.Should().NotBeNull();
            result!.Orders.Should().NotBeNull();
        }

        [Fact]
        public async Task UpdatePrice_Sandbox_ReturnsSuccessResponse()
        {
            SkipIfNoCredentials();

            // Usa un SKU de prueba que exista en tu cuenta sandbox de Miravia
            var sandboxSku = Env("MIRAVIA_SANDBOX_TEST_SKU", "TEST-SKU-001");

            var payload = new
            {
                skus = new[] { new { SkuId = sandboxSku, SalePrice = 19.99 } }
            };

            var result = await Client.PostJsonPayloadAsync("/product/price/update", payload);

            result.Should().NotBeNull();
            result!.Code.Should().Be("0");
        }

        [Fact]
        public async Task UpdateStock_Sandbox_ReturnsSuccessResponse()
        {
            SkipIfNoCredentials();

            var sandboxSku = Env("MIRAVIA_SANDBOX_TEST_SKU", "TEST-SKU-001");

            var payload = new
            {
                skus = new[] { new { SkuId = sandboxSku, Quantity = 50 } }
            };

            var result = await Client.PostJsonPayloadAsync("/product/stock/update", payload);

            result.Should().NotBeNull();
            result!.Code.Should().Be("0");
        }

        [Fact]
        public async Task GetOrderItems_Sandbox_ForExistingOrder()
        {
            SkipIfNoCredentials();

            // Primero obtiene un pedido real del sandbox
            var orders = await Client.GetAsync<MiraviaOrdersData>("/orders/get",
                new Dictionary<string, string>
                {
                    ["created_after"]  = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss"),
                    ["created_before"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["limit"]          = "1",
                    ["offset"]         = "0"
                });

            if (orders?.Orders == null || orders.Orders.Length == 0)
                throw new SkipException("No hay pedidos en el sandbox. Crea uno desde App Console → Testing Tools.");

            var orderId = orders.Orders[0].OrderId;

            var items = await Client.GetAsync<MiraviaOrderItemsData>("/order/items/get",
                new Dictionary<string, string>
                {
                    ["order_id_list"] = $"[{orderId}]"
                });

            items.Should().NotBeNull();
            items!.OrderItems.Should().NotBeNull();
        }

        [Fact]
        public async Task InvalidToken_Sandbox_ThrowsMiraviaApiException()
        {
            var badClient = new MiraviaApiClient("bad_key", "bad_secret", "bad_token", useSandbox: true);

            var act = async () => await badClient.GetAsync<MiraviaOrdersData>("/orders/get",
                new Dictionary<string, string>
                {
                    ["created_after"]  = "2025-01-01 00:00:00",
                    ["created_before"] = "2025-01-31 23:59:59"
                });

            await act.Should().ThrowAsync<MiraviaApiException>();
        }
    }

    /// <summary>Excepción para saltar un test cuando faltan prerequisitos (similar a Skip en NUnit).</summary>
    public class SkipException : Exception
    {
        public SkipException(string reason) : base(reason) { }
    }
}
