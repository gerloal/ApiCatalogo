using System;

namespace FuncionLambda.Models
{
    public class OrderLine
    {
        public string AmazonOrderId { get; set; }
        public string OrderItemId { get; set; }
        public string ASIN { get; set; }
        public string SellerSKU { get; set; }
        public string Title { get; set; }
        public int QuantityOrdered { get; set; }
        public int QuantityShipped { get; set; }
        public decimal ItemPrice { get; set; }
        public string Currency { get; set; }
        public decimal ItemTax { get; set; }
        public decimal ShippingPrice { get; set; }
        public decimal ShippingTax { get; set; }
    }
}
