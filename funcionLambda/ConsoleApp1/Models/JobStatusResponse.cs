namespace FuncionLambda.Models
{
    public class JobStatusResponse
    {
        public string JobId { get; set; }
        public string TenantId { get; set; }
        public string Status { get; set; }
        public int TotalOrders { get; set; }
        public int TotalLines { get; set; }
        public string HeadersPresignedUrl { get; set; }
        public string LinesPresignedUrl { get; set; }
        public string ErrorMessage { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
    }
}
