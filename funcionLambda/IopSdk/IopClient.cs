using Iop.Api.Util;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Iop.Api
{
    public class IopClient : IIopClient
    {
        internal string serverUrl;
        internal string appKey;
        internal string appSecret;
        internal string signMethod = Constants.SIGN_METHOD_SHA256;
        internal string sdkVersion = "iop-sdk-net-20180508";
        internal string logLevel = Constants.LOG_LEVEL_ERROR;

        internal readonly DateTime dt1970 = new DateTime(1970, 1, 1, 0, 0, 0, 0);
        internal WebUtils webUtils;
        internal IIopLogger topLogger;
        internal bool disableTrace = false;
        internal IDictionary<string, string>? customrParameters;

        public IopClient(string serverUrl, string appKey, string appSecret)
        {
            this.serverUrl = serverUrl;
            this.appKey = appKey;
            this.appSecret = appSecret;
            this.webUtils = new WebUtils();
            this.topLogger = IopLogger.Instance;
        }

        public void SetTimeout(int timeout) => webUtils.Timeout = timeout;
        public void SetReadWriteTimeout(int t) => webUtils.ReadWriteTimeout = t;
        public void SetSignMethod(string sm) { if (sm == Constants.SIGN_METHOD_HMAC || sm == Constants.SIGN_METHOD_SHA256) signMethod = sm; }
        public void SetDisableTrace(bool v) => disableTrace = v;
        public void SetIgnoreSSLCheck(bool v) => webUtils.IgnoreSSLCheck = v;
        public void SetDisableWebProxy(bool v) => webUtils.DisableWebProxy = v;
        public void SetLogLevel(string level) => logLevel = level;
        public void SetCustomParameters(IDictionary<string, string> p) => customrParameters = p;

        public virtual IopResponse Execute(IopRequest request) => DoExecute(request, null, DateTime.UtcNow);
        public virtual IopResponse Execute(IopRequest request, string accessToken) => DoExecute(request, accessToken, DateTime.UtcNow);
        public virtual IopResponse Execute(IopRequest request, string accessToken, DateTime timestamp) => DoExecute(request, accessToken, timestamp);

        private IopResponse DoExecute(IopRequest request, string? accessToken, DateTime timestamp)
        {
            long start = DateTime.Now.Ticks;

            var txtParams = new IopDictionary(request.GetParameters());
            txtParams.Add(Constants.APP_KEY, appKey);
            txtParams.Add(Constants.TIMESTAMP, GetTimestamp(timestamp));
            txtParams.Add(Constants.ACCESS_TOKEN, accessToken);
            txtParams.Add(Constants.PARTNER_ID, sdkVersion);
            if (customrParameters != null) txtParams.AddAll(customrParameters);
            txtParams.Add(Constants.SIGN_METHOD, signMethod);
            if (logLevel == Constants.LOG_LEVEL_DEBUG) txtParams.Add(Constants.DEBUG, true);

            txtParams.Add(Constants.SIGN, IopUtils.SignRequest(request.GetApiName() ?? "", txtParams, appSecret, signMethod));

            string realServerUrl = GetServerUrl(serverUrl, request.GetApiName(), accessToken);

            try
            {
                string body;
                if (request.GetFileParameters() != null)
                    body = webUtils.DoPost(realServerUrl, txtParams, request.GetFileParameters(), request.GetHeaderParameters());
                else if (request.GetHttpMethod() == Constants.METHOD_POST)
                    body = webUtils.DoPost(realServerUrl, txtParams, request.GetHeaderParameters());
                else
                    body = webUtils.DoGet(realServerUrl, txtParams, request.GetHeaderParameters());

                var response = ParseResponse(body);

                if (response.IsError() || logLevel != Constants.LOG_LEVEL_ERROR)
                {
                    double latency = new TimeSpan(DateTime.Now.Ticks - start).TotalMilliseconds;
                    LogApiError(appKey, sdkVersion, request.GetApiName() ?? "", serverUrl, txtParams, latency, response.Body ?? "");
                }

                return response;
            }
            catch (Exception e)
            {
                double latency = new TimeSpan(DateTime.Now.Ticks - start).TotalMilliseconds;
                LogApiError(appKey, sdkVersion, request.GetApiName() ?? "", serverUrl, txtParams, latency, e.GetType() + ": " + e.Message);
                throw;
            }
        }

        private IopResponse ParseResponse(string jsonRsp)
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonRsp);
            var rsp = new IopResponse { Body = jsonRsp };
            if (root != null)
            {
                rsp.Type = GetStringValue(root, Constants.RSP_TYPE);
                rsp.Code = GetStringValue(root, Constants.RSP_CODE);
                rsp.Message = GetStringValue(root, Constants.RSP_MSG);
                rsp.RequestId = GetStringValue(root, Constants.RSP_REQUEST_ID);
            }
            return rsp;
        }

        private static string? GetStringValue(Dictionary<string, JsonElement> root, string key)
            => root.TryGetValue(key, out var val) ? val.GetString() : null;

        private long GetTimestamp(DateTime dt) => (dt.Ticks - dt1970.Ticks) / 10000;

        internal virtual string GetServerUrl(string baseUrl, string? apiName, string? session)
        {
            if (string.IsNullOrEmpty(apiName)) return baseUrl;
            return baseUrl.EndsWith('/') ? baseUrl + apiName!.Substring(1) : baseUrl + apiName;
        }

        internal void LogApiError(string appKey, string sdkVersion, string apiName, string url,
            Dictionary<string, string> parameters, double latency, string errorMessage)
        {
            if (!disableTrace)
                topLogger.TraceApiError(appKey, sdkVersion, apiName, url, parameters, latency, errorMessage);
        }
    }
}
