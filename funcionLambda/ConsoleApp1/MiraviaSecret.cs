namespace FuncionLambda
{
    /// <summary>
    /// Credenciales para la Miravia Open Platform API.
    /// Se almacenan en Secrets Manager con el path:
    /// /catalog-api/{env}/tenants/{tenantId}/miravia
    /// </summary>
    public class MiraviaSecret
    {
        public string AppKey { get; set; } = string.Empty;
        public string AppSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string ClientPartnerEmail { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
    }
}
