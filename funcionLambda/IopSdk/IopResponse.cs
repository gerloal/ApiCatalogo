using System;

namespace Iop.Api
{
    [Serializable]
    public class IopResponse
    {
        public string? Type { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
        public string? RequestId { get; set; }
        public string? Body { get; set; }

        public bool IsError() => Code != null && !Code.Equals("0");
    }
}
