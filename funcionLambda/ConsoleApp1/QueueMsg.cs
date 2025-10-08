using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuncionLambda
{
    public class QueueMsg
    {
        public string? tenantId { get; set; }
        public string? jobId { get; set; }
        public string? bucket { get; set; }
        public string? key { get; set; }
        public string? operation { get; set; }
        public string? contentType { get; set; }
    }
}
