using Iop.Api.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace Iop.Api
{
    /// <summary>
    /// Lambda-friendly logger: writes to Console (CloudWatch).
    /// </summary>
    public class IopLogger : IIopLogger
    {
        private static IopLogger? _instance;
        private static readonly object _lock = new();

        public static IopLogger Instance
        {
            get
            {
                if (_instance == null)
                    lock (_lock) { _instance ??= new IopLogger(); }
                return _instance;
            }
        }

        private IopLogger() { }

        public void TraceApiError(string appKey, string sdkVersion, string apiName, string url,
            Dictionary<string, string> parameters, double latency, string errorMessage)
        {
            var info = new StringBuilder();
            info.Append(appKey).Append(Constants.LOG_SPLIT)
                .Append(sdkVersion).Append(Constants.LOG_SPLIT)
                .Append(apiName).Append(Constants.LOG_SPLIT)
                .Append(latency).Append(Constants.LOG_SPLIT)
                .Append(url).Append(Constants.LOG_SPLIT)
                .Append(WebUtils.BuildQuery(parameters)).Append(Constants.LOG_SPLIT)
                .Append(errorMessage);
            Console.WriteLine($"[IopSdk] {info}");
        }
    }
}
