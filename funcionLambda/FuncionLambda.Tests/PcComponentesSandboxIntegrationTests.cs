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
    /// Tests de integración contra el sandbox de PcComponentes (Mirakl MMP).
    ///
    /// PREREQUISITOS:
    ///   1. Obtener credenciales sandbox de PcComponentes Marketplace (contactar con el equipo de integración)
    ///   2. Rellenar las variables de entorno:
    ///        PCCOMPONENTES_SANDBOX_API_KEY     → API Key del entorno sandbox
    ///        PCCOMPONENTES_SANDBOX_BASE_URL    → URL base del sandbox (ej. https://xxx-sandbox.mirakl.net)
    ///        PCCOMPONENTES_SANDBOX_TEST_SKU    → SKU de un producto existente en el sandbox (opcional)
    ///
    /// EJECUCIÓN SOLO INTEGRATION:
    ///   dotnet test --filter "Category=Integration"
    ///
    /// EJECUCIÓN SIN INTEGRATION (CI normal):
    ///   dotnet test --filter "Category!=Integration"
    /// </summary>
    [Trait("Category", "Integration")]
    public class PcComponentesSandboxIntegrationTests
    {
        // ── Credenciales sandbox ──────────────────────────────────────
        // Rellenar aquí o via variables de entorno
        private static readonly string ApiKey     = Env("PCCOMPONENTES_SANDBOX_API_KEY",  "");
        private static readonly string BaseUrl    = Env("PCCOMPONENTES_SANDBOX_BASE_URL", "");
        private static readonly string TestSku    = Env("PCCOMPONENTES_SANDBOX_TEST_SKU", "TEST-SKU-001");

        private static string Env(string name, string fallback)
            => Environment.GetEnvironmentVariable(name) ?? fallback;

        private PcComponentesApiClient Client => new(ApiKey, BaseUrl);

        // ── Guardia ───────────────────────────────────────────────────

        private void SkipIfNoCredentials()
        {
            if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(BaseUrl))
                throw new SkipException(
                    "Credenciales sandbox no configuradas. " +
                    "Definir PCCOMPONENTES_SANDBOX_API_KEY y PCCOMPONENTES_SANDBOX_BASE_URL.");
        }

        // ── Tests ─────────────────────────────────────────────────────

        [Fact]
        public async Task GetOrders_Sandbox_ReturnsSuccessResponse()
        {
            SkipIfNoCredentials();

            var result = await Client.GetOrdersAsync(
                startDate: DateTime.UtcNow.AddDays(-30),
                endDate:   DateTime.UtcNow,
                offset:    0,
                max:       10);

            result.Should().NotBeNull();
            result!.Orders.Should().NotBeNull();
        }

        [Fact]
        public async Task GetOrders_Sandbox_TotalCountIsConsistent()
        {
            SkipIfNoCredentials();

            var result = await Client.GetOrdersAsync(
                startDate: DateTime.UtcNow.AddDays(-30),
                endDate:   DateTime.UtcNow,
                offset:    0,
                max:       100);

            result.Should().NotBeNull();
            result!.TotalCount.Should().BeGreaterThanOrEqualTo(result.Orders.Length);
        }

        [Fact]
        public async Task ImportOffers_Sandbox_ReturnsImportId()
        {
            SkipIfNoCredentials();

            // CSV mínimo con la cabecera y una línea de prueba
            var csv = $"sku;price;quantity\n{TestSku};19.99;10";
            var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

            var result = await Client.ImportOffersAsync(stream, importMode: "NORMAL");

            result.Should().NotBeNull();
            result!.ImportId.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task GetImportStatus_Sandbox_ReturnsStatus()
        {
            SkipIfNoCredentials();

            // Primero genera una importación para obtener un import_id real
            var csv    = $"sku;price;quantity\n{TestSku};19.99;10";
            var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
            var import = await Client.ImportOffersAsync(stream);
            import.Should().NotBeNull();

            // Consulta el estado
            var status = await Client.GetImportStatusAsync(import!.ImportId);

            status.Should().NotBeNull();
            status!.Status.Should().BeOneOf("WAITING", "RUNNING", "COMPLETE", "FAILED");
        }

        [Fact]
        public async Task StartAsyncOrderExport_Sandbox_ReturnsTrackingId()
        {
            SkipIfNoCredentials();

            var result = await Client.StartAsyncOrderExportAsync(
                startDate: DateTime.UtcNow.AddDays(-30),
                endDate:   DateTime.UtcNow);

            result.Should().NotBeNull();
            result!.TrackingId.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task GetAsyncExportStatus_Sandbox_ReturnsKnownStatus()
        {
            SkipIfNoCredentials();

            // Inicia exportación y obtiene el tracking_id
            var export = await Client.StartAsyncOrderExportAsync(
                DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            export.Should().NotBeNull();

            // Consulta el estado con el tracking_id obtenido
            var status = await Client.GetAsyncExportStatusAsync(export!.TrackingId);

            status.Should().NotBeNull();
            status!.Status.Should().BeOneOf("WAITING", "RUNNING", "COMPLETE", "FAILED");
        }
    }
}
