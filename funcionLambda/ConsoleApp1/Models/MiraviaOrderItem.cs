using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    /// <summary>
    /// Representa una línea de pedido de la Miravia Open Platform API.
    /// Mapea los campos devueltos por /order/items/get.
    /// </summary>
    public class MiraviaOrderItem
    {
        [JsonPropertyName("order_item_id")]
        public long OrderItemId { get; set; }

        [JsonPropertyName("order_id")]
        public long OrderId { get; set; }

        [JsonPropertyName("sku")]
        public string Sku { get; set; } = string.Empty;

        [JsonPropertyName("shop_sku")]
        public string ShopSku { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("variation")]
        public string Variation { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("paid_price")]
        public string PaidPrice { get; set; } = "0";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("item_price")]
        public string ItemPrice { get; set; } = "0";

        [JsonPropertyName("shipping_fee")]
        public string ShippingFee { get; set; } = "0";

        [JsonPropertyName("tax_amount")]
        public string TaxAmount { get; set; } = "0";

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>Respuesta de /order/items/get</summary>
    public class MiraviaOrderItemsData
    {
        [JsonPropertyName("order_items")]
        public MiraviaOrderItem[] OrderItems { get; set; } = [];
    }
}
