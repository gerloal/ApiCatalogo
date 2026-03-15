using System.Text.Json.Serialization;

namespace FuncionLambda.Models
{
    /// <summary>
    /// Respuesta de GET /api/orders (Mirakl OR11).
    /// </summary>
    public class MiraklOrdersResponse
    {
        [JsonPropertyName("orders")]
        public MiraklOrder[] Orders { get; set; } = System.Array.Empty<MiraklOrder>();

        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }
    }

    public class MiraklOrder
    {
        [JsonPropertyName("order_id")]
        public string OrderId { get; set; } = string.Empty;

        [JsonPropertyName("commercial_id")]
        public string CommercialId { get; set; } = string.Empty;

        [JsonPropertyName("created_date")]
        public string CreatedDate { get; set; } = string.Empty;

        [JsonPropertyName("last_updated_date")]
        public string LastUpdatedDate { get; set; } = string.Empty;

        [JsonPropertyName("order_state")]
        public string OrderState { get; set; } = string.Empty;

        [JsonPropertyName("total_price")]
        public decimal TotalPrice { get; set; }

        [JsonPropertyName("currency_iso_code")]
        public string CurrencyIsoCode { get; set; } = string.Empty;

        [JsonPropertyName("order_lines")]
        public MiraklOrderLine[] OrderLines { get; set; } = System.Array.Empty<MiraklOrderLine>();

        [JsonPropertyName("customer")]
        public MiraklCustomer? Customer { get; set; }

        [JsonPropertyName("shipping")]
        public MiraklShipping? Shipping { get; set; }
    }

    public class MiraklOrderLine
    {
        [JsonPropertyName("order_line_id")]
        public string OrderLineId { get; set; } = string.Empty;

        [JsonPropertyName("offer_sku")]
        public string OfferSku { get; set; } = string.Empty;

        [JsonPropertyName("product_title")]
        public string ProductTitle { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("price_unit")]
        public decimal PriceUnit { get; set; }

        [JsonPropertyName("currency_iso_code")]
        public string CurrencyIsoCode { get; set; } = string.Empty;

        [JsonPropertyName("order_line_state")]
        public string OrderLineState { get; set; } = string.Empty;

        /// <summary>
        /// Relleno programáticamente con el OrderId del pedido padre al recopilar líneas.
        /// No forma parte de la respuesta JSON de Mirakl.
        /// </summary>
        [JsonIgnore]
        public string ParentOrderId { get; set; } = string.Empty;
    }

    public class MiraklCustomer
    {
        [JsonPropertyName("customer_id")]
        public string CustomerId { get; set; } = string.Empty;

        [JsonPropertyName("firstname")]
        public string Firstname { get; set; } = string.Empty;

        [JsonPropertyName("lastname")]
        public string Lastname { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("billing_address")]
        public MiraklAddress? BillingAddress { get; set; }

        [JsonPropertyName("shipping_address")]
        public MiraklAddress? ShippingAddress { get; set; }
    }

    public class MiraklAddress
    {
        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("country_iso_code")]
        public string CountryIsoCode { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("street_1")]
        public string Street1 { get; set; } = string.Empty;

        [JsonPropertyName("street_2")]
        public string Street2 { get; set; } = string.Empty;

        [JsonPropertyName("zip_code")]
        public string ZipCode { get; set; } = string.Empty;
    }

    public class MiraklShipping
    {
        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("currency_iso_code")]
        public string CurrencyIsoCode { get; set; } = string.Empty;
    }
}
