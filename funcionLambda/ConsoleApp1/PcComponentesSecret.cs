using System.Text.Json.Serialization;

namespace FuncionLambda
{
    /// <summary>
    /// Credenciales para la API Mirakl de PcComponentes.
    /// Se almacenan en Secrets Manager con el path:
    /// /catalog-api/{env}/tenants/{tenantId}/pccomponentes
    /// </summary>
    public class PcComponentesSecret
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>URL base de la instancia Mirakl de PcComponentes, ej. https://pccomponentes.mirakl.net</summary>
        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>URL del entorno sandbox/staging de PcComponentes cuando UseSandbox=true</summary>
        [JsonPropertyName("sandboxBaseUrl")]
        public string SandboxBaseUrl { get; set; } = string.Empty;

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;

        [JsonPropertyName("clientEmail")]
        public string ClientEmail { get; set; } = string.Empty;

        [JsonPropertyName("clientPartnerEmail")]
        public string ClientPartnerEmail { get; set; } = string.Empty;
    }
}
