namespace FuncionLambda.Models
{
    public class SpApiSecret
    {
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
        public string? RoleArn { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? RefreshToken { get; set; }
        public string? MarketPlaceID { get; set; }
        public string? TenantId { get; set; }
        public string? SellerId { get; set; }
    }

    public class MiraviaSecret
    {
        /// <summary>App Key obtenida en Miravia OpenPlatform al crear la app.</summary>
        public string? AppKey { get; set; }

        /// <summary>App Secret obtenido en Miravia OpenPlatform.</summary>
        public string? AppSecret { get; set; }

        /// <summary>Access Token del seller (obtenido mediante OAuth).</summary>
        public string? AccessToken { get; set; }

        /// <summary>Identificador del tenant en nuestro sistema.</summary>
        public string? TenantId { get; set; }
    }
}
