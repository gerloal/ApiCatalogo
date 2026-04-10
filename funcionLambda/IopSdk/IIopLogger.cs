using System.Collections.Generic;

namespace Iop.Api
{
    public interface IIopLogger
    {
        void TraceApiError(string appKey, string sdkVersion, string apiName, string url,
            Dictionary<string, string> parameters, double latency, string errorMessage);
    }
}
