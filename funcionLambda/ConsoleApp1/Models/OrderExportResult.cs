namespace FuncionLambda.Models
{
    public class OrderExportResult
    {
        public string JobId { get; set; }
        public string TenantId { get; set; }
        public string HeadersFileKey { get; set; }
        public string LinesFileKey { get; set; }
        public string HeadersPresignedUrl { get; set; }
        public string LinesPresignedUrl { get; set; }
        public int TotalOrders { get; set; }
        public int TotalLines { get; set; }
        public string Status { get; set; }
    }
}
