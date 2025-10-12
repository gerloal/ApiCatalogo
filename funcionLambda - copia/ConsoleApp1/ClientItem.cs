using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuncionLambda
{
    public class ClientItem
    {
        public string Sku { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal? minPrice { get; set; } = null;
        public decimal? maxPrice { get; set; } = null;
        public int? Stock { get; set; }
        public int? leadTimeToShip { get; set; } = null;
        public DateTime? restockDate { get; set; } = null;
    }
}
