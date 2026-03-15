using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    /// <summary>
    /// Respuesta de POST /api/offers/imports (Mirakl OF01).
    /// </summary>
    public class MiraklImportResult
    {
        [JsonPropertyName("import_id")]
        public long ImportId { get; set; }

        [JsonPropertyName("product_import_id")]
        public long? ProductImportId { get; set; }
    }
}
