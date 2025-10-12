using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuncionLambda
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
    };
}
