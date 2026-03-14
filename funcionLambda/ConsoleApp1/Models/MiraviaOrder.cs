using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    /// <summary>
    /// Representa un pedido de la Miravia Open Platform API.
    /// Mapea los campos devueltos por /orders/get.
    /// </summary>
    public class MiraviaOrder
    {
        [JsonPropertyName("order_id")]
        public long OrderId { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public string Price { get; set; } = "0";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("shipping_fee")]
        public string ShippingFee { get; set; } = "0";

        [JsonPropertyName("payment_method")]
        public string PaymentMethod { get; set; } = string.Empty;

        [JsonPropertyName("address_info")]
        public MiraviaAddressInfo? AddressInfo { get; set; }
    }

    public class MiraviaAddressInfo
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("address2")]
        public string Address2 { get; set; } = string.Empty;

        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("post_code")]
        public string PostCode { get; set; } = string.Empty;

        [JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;

        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;
    }

    /// <summary>Respuesta de /orders/get</summary>
    public class MiraviaOrdersData
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("orders")]
        public MiraviaOrder[] Orders { get; set; } = [];
    }
}
