namespace FuncionLambda
{
    /// <summary>
    /// Credenciales para la AliExpress Open Platform API (IOP).
    /// Se almacenan en Secrets Manager con el path:
    /// /catalog-api/{env}/tenants/{tenantId}/aliexpress
    /// </summary>
    public class AliExpressSecret
    {
        public string AppKey { get; set; } = string.Empty;
        public string AppSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string ClientPartnerEmail { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
    }
}
