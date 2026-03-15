using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    /// <summary>
    /// Respuesta de GET /api/offers/imports/{import} (Mirakl OF02).
    /// Status: WAITING_SYNCHRONIZATION_PRODUCT | WAITING | RUNNING | COMPLETE | FAILED
    /// </summary>
    public class MiraklImportStatus
    {
        [JsonPropertyName("date_created")]
        public string DateCreated { get; set; } = string.Empty;

        [JsonPropertyName("has_error_report")]
        public bool HasErrorReport { get; set; }

        [JsonPropertyName("import_id")]
        public long ImportId { get; set; }

        [JsonPropertyName("lines_in_error")]
        public int LinesInError { get; set; }

        [JsonPropertyName("lines_in_pending")]
        public int LinesInPending { get; set; }

        [JsonPropertyName("lines_in_success")]
        public int LinesInSuccess { get; set; }

        [JsonPropertyName("lines_read")]
        public int LinesRead { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = string.Empty;

        [JsonPropertyName("offer_deleted")]
        public int OfferDeleted { get; set; }

        [JsonPropertyName("offer_inserted")]
        public int OfferInserted { get; set; }

        [JsonPropertyName("offer_updated")]
        public int OfferUpdated { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }
}
