using FuncionLambda.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    /// <summary>
    /// Cliente HTTP para la API Mirakl de PcComponentes.
    /// Autenticación mediante API Key en el header Authorization (sin prefijo Bearer).
    ///
    /// API Reference: https://developer.mirakl.com/content/product/mmp/rest/seller/openapi3
    /// Doc PcComponentes: https://marketplacehelp.pccomponentes.com/hc/es-es/articles/20560979139997
    /// </summary>
    public class PcComponentesApiClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <param name="secret">Credenciales PcComponentes desde Secrets Manager</param>
        /// <param name="useSandbox">true → usa SandboxBaseUrl del secreto (si está definida)</param>
        /// <param name="httpClient">Inyectar para tests; null crea una instancia nueva</param>
        public PcComponentesApiClient(PcComponentesSecret secret, bool useSandbox = false, HttpClient? httpClient = null)
            : this(
                secret.ApiKey,
                useSandbox && !string.IsNullOrEmpty(secret.SandboxBaseUrl)
                    ? secret.SandboxBaseUrl
                    : secret.BaseUrl,
                httpClient)
        { }

        public PcComponentesApiClient(string apiKey, string baseUrl, HttpClient? httpClient = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = httpClient ?? new HttpClient();
            // Mirakl usa la API key directamente como valor del header Authorization
            _http.DefaultRequestHeaders.Add("Authorization", apiKey);
        }

        /// <summary>
        /// OF01 — Importa un fichero CSV para crear/actualizar ofertas (precios y stock).
        /// POST /api/offers/imports
        /// Max: 1 llamada/minuto. Recomendado: cada 5 minutos.
        /// </summary>
        public async Task<MiraklImportResult?> ImportOffersAsync(
            Stream csvContent,
            string fileName = "catalog.csv",
            string importMode = "NORMAL")
        {
            using var content = new MultipartFormDataContent();

            var fileContent = new StreamContent(csvContent);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(importMode), "import_mode");

            var response = await _http.PostAsync($"{_baseUrl}/api/offers/imports", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklImportResult>(json, JsonOptions);
        }

        /// <summary>
        /// OF02 — Obtiene el estado de un import de ofertas.
        /// GET /api/offers/imports/{importId}
        /// Status posibles: WAITING | RUNNING | COMPLETE | FAILED | WAITING_SYNCHRONIZATION_PRODUCT
        /// </summary>
        public async Task<MiraklImportStatus?> GetImportStatusAsync(long importId)
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/offers/imports/{importId}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklImportStatus>(json, JsonOptions);
        }

        /// <summary>
        /// OR11 — Lista pedidos con paginación (offset).
        /// GET /api/orders
        /// </summary>
        public async Task<MiraklOrdersResponse?> GetOrdersAsync(
            DateTime startDate,
            DateTime endDate,
            int offset = 0,
            int max = 100)
        {
            var start = Uri.EscapeDataString(startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            var end   = Uri.EscapeDataString(endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            var url   = $"{_baseUrl}/api/orders?start_date={start}&end_date={end}&max={max}&offset={offset}";

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklOrdersResponse>(json, JsonOptions);
        }

        /// <summary>
        /// OR13 — Inicia un export asíncrono de pedidos.
        /// POST /api/orders/async-export
        /// </summary>
        public async Task<MiraklAsyncExportResult?> StartAsyncOrderExportAsync(
            DateTime startDate,
            DateTime endDate)
        {
            var body = JsonSerializer.Serialize(new
            {
                start_date = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                end_date   = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            });

            var content  = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_baseUrl}/api/orders/async-export", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklAsyncExportResult>(json, JsonOptions);
        }

        /// <summary>
        /// OR14 — Obtiene el estado de un export asíncrono.
        /// GET /api/orders/async-export/status/{trackingId}
        /// Cuando status=COMPLETE, DownloadUrl contiene la URL de descarga del fichero.
        /// </summary>
        public async Task<MiraklAsyncExportStatus?> GetAsyncExportStatusAsync(string trackingId)
        {
            var response = await _http.GetAsync(
                $"{_baseUrl}/api/orders/async-export/status/{Uri.EscapeDataString(trackingId)}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklAsyncExportStatus>(json, JsonOptions);
        }
    }
}
