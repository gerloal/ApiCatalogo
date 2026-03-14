namespace FuncionLambda.Models
{
    public class ExportOrdersQueueMessage
    {
        public string TenantId { get; set; }
        public string JobId { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string Format { get; set; }
        public string Operation { get; set; } = "EXPORT_ORDERS";
    }
}
