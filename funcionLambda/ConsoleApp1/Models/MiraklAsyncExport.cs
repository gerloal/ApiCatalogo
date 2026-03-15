using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    /// <summary>
    /// Respuesta de POST /api/orders/async-export (Mirakl OR13).
    /// </summary>
    public class MiraklAsyncExportResult
    {
        [JsonPropertyName("tracking_id")]
        public string TrackingId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta de GET /api/orders/async-export/status/{tracking_id} (Mirakl OR14).
    /// Status: QUEUED | RUNNING | COMPLETE | FAILED
    /// </summary>
    public class MiraklAsyncExportStatus
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("error_message")]
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
