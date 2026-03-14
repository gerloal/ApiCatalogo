using System;
using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    public class ExportOrdersRequest
    {
        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; }
        
        [JsonPropertyName("startDate")]
        public DateTime StartDate { get; set; }
        
        [JsonPropertyName("endDate")]
        public DateTime EndDate { get; set; }
        
        [JsonPropertyName("format")]
        public string Format { get; set; } = "CSV";
    }
}
