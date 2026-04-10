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
    /// Cliente HTTP para la API Mirakl de Decathlon.
    /// Mismo protocolo Mirakl que PcComponentes: API Key en header Authorization.
    ///
    /// Decathlon Marketplace API: https://developer.mirakl.com/content/product/mmp/rest/seller/openapi3
    /// </summary>
    public class DecathlonApiClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public DecathlonApiClient(DecathlonSecret secret, bool useSandbox = false, HttpClient? httpClient = null)
            : this(
                secret.ApiKey,
                useSandbox && !string.IsNullOrEmpty(secret.SandboxBaseUrl)
                    ? secret.SandboxBaseUrl
                    : secret.BaseUrl,
                httpClient)
        { }

        public DecathlonApiClient(string apiKey, string baseUrl, HttpClient? httpClient = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http    = httpClient ?? new HttpClient();
            _http.DefaultRequestHeaders.Add("Authorization", apiKey);
        }

        /// <summary>
        /// OF01 — Importa CSV con ofertas (precios y stock).
        /// POST /api/offers/imports
        /// </summary>
        public async Task<MiraklImportResult?> ImportOffersAsync(
            Stream csvContent,
            string fileName   = "catalog.csv",
            string importMode = "NORMAL")
        {
            using var content     = new MultipartFormDataContent();
            var fileContent       = new StreamContent(csvContent);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(importMode), "import_mode");

            var response = await _http.PostAsync($"{_baseUrl}/api/offers/imports", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklImportResult>(json, JsonOptions);
        }

        /// <summary>
        /// OF02 — Estado de un import de ofertas.
        /// GET /api/offers/imports/{importId}
        /// </summary>
        public async Task<MiraklImportStatus?> GetImportStatusAsync(long importId)
        {
            var response = await _http.GetAsync($"{_baseUrl}/api/offers/imports/{importId}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MiraklImportStatus>(json, JsonOptions);
        }

        /// <summary>
        /// OR11 — Lista pedidos con paginación.
        /// GET /api/orders
        /// </summary>
        public async Task<MiraklOrdersResponse?> GetOrdersAsync(
            DateTime startDate,
            DateTime endDate,
            int offset = 0,
            int max    = 100)
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
        /// OR13 — Inicia export asíncrono de pedidos.
        /// POST /api/orders/async-export
        /// </summary>
        public async Task<MiraklAsyncExportResult?> StartAsyncOrderExportAsync(DateTime startDate, DateTime endDate)
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
        /// OR14 — Estado de un export asíncrono.
        /// GET /api/orders/async-export/status/{trackingId}
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
