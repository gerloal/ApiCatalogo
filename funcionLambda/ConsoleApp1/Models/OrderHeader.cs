using System;

namespace FuncionLambda.Models
{
    public class OrderHeader
    {
        public string AmazonOrderId { get; set; }
        public string PurchaseDate { get; set; }
        public string OrderStatus { get; set; }
        public decimal OrderTotal { get; set; }
        public string Currency { get; set; }
        public string BuyerEmail { get; set; }
        public string BuyerName { get; set; }
        public string ShipServiceLevel { get; set; }
        public string ShipmentServiceLevelCategory { get; set; }
        public string ShippingAddress { get; set; }
        public string City { get; set; }
        public string StateOrRegion { get; set; }
        public string PostalCode { get; set; }
        public string CountryCode { get; set; }
    }
}
