using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Iop.Api.Util
{
    public sealed class WebUtils
    {
        private int _timeout = 20000;
        private int _readWriteTimeout = 60000;
        private bool _ignoreSSLCheck = true;
        private bool _disableWebProxy = false;

        public int Timeout { get => _timeout; set => _timeout = value; }
        public int ReadWriteTimeout { get => _readWriteTimeout; set => _readWriteTimeout = value; }
        public bool IgnoreSSLCheck { get => _ignoreSSLCheck; set => _ignoreSSLCheck = value; }
        public bool DisableWebProxy { get => _disableWebProxy; set => _disableWebProxy = value; }

        public string DoPost(string url, IDictionary<string, string> textParams)
            => DoPost(url, textParams, (IDictionary<string, string>?)null);

        public string DoPost(string url, IDictionary<string, string> textParams, IDictionary<string, string>? headerParams)
        {
            var req = GetWebRequest(url, "POST", headerParams);
            req.ContentType = "application/x-www-form-urlencoded;charset=utf-8";

            byte[] postData = Encoding.UTF8.GetBytes(BuildQuery(textParams));
            using var reqStream = req.GetRequestStream();
            reqStream.Write(postData, 0, postData.Length);

            var rsp = (HttpWebResponse)req.GetResponse();
            return GetResponseAsString(rsp, GetResponseEncoding(rsp));
        }

        public string DoGet(string url, IDictionary<string, string> textParams)
            => DoGet(url, textParams, null);

        public string DoGet(string url, IDictionary<string, string> textParams, IDictionary<string, string>? headerParams)
        {
            if (textParams != null && textParams.Count > 0)
                url = BuildRequestUrl(url, textParams);

            var req = GetWebRequest(url, "GET", headerParams);
            req.ContentType = "application/x-www-form-urlencoded;charset=utf-8";

            var rsp = (HttpWebResponse)req.GetResponse();
            return GetResponseAsString(rsp, GetResponseEncoding(rsp));
        }

        public string DoPost(string url, IDictionary<string, string> textParams, IDictionary<string, FileItem>? fileParams, IDictionary<string, string>? headerParams)
        {
            if (fileParams == null || fileParams.Count == 0)
                return DoPost(url, textParams, headerParams);

            string boundary = DateTime.Now.Ticks.ToString("X");
            var req = GetWebRequest(url, "POST", headerParams);
            req.ContentType = $"multipart/form-data;charset=utf-8;boundary={boundary}";

            using var reqStream = req.GetRequestStream();
            byte[] itemBoundaryBytes = Encoding.UTF8.GetBytes($"\r\n--{boundary}\r\n");
            byte[] endBoundaryBytes = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");

            const string textTemplate = "Content-Disposition:form-data;name=\"{0}\"\r\nContent-Type:text/plain\r\n\r\n{1}";
            foreach (var kv in textParams)
            {
                byte[] itemBytes = Encoding.UTF8.GetBytes(string.Format(textTemplate, kv.Key, kv.Value));
                reqStream.Write(itemBoundaryBytes);
                reqStream.Write(itemBytes);
            }

            const string fileTemplate = "Content-Disposition:form-data;name=\"{0}\";filename=\"{1}\"\r\nContent-Type:{2}\r\n\r\n";
            foreach (var kv in fileParams)
            {
                FileItem fileItem = kv.Value;
                if (!fileItem.IsValid()) throw new ArgumentException("FileItem is invalid");

                byte[] itemBytes = Encoding.UTF8.GetBytes(string.Format(fileTemplate, kv.Key, fileItem.GetFileName(), fileItem.GetMimeType()));
                reqStream.Write(itemBoundaryBytes);
                reqStream.Write(itemBytes);
                fileItem.Write(reqStream);
            }

            reqStream.Write(endBoundaryBytes);

            var rsp = (HttpWebResponse)req.GetResponse();
            return GetResponseAsString(rsp, GetResponseEncoding(rsp));
        }

        public HttpWebRequest GetWebRequest(string url, string method, IDictionary<string, string>? headerParams)
        {
#pragma warning disable SYSLIB0014
            var req = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014

            if (_ignoreSSLCheck)
            {
                req.ServerCertificateValidationCallback = (_, _, _, _) => true;
            }

            if (_disableWebProxy) req.Proxy = null;

            if (headerParams != null)
            {
                foreach (var kv in headerParams)
                    req.Headers.Add(kv.Key, kv.Value);
            }

            req.Method = method;
            req.KeepAlive = true;
            req.UserAgent = Constants.SDK_VERSION;
            req.Accept = "text/xml,text/javascript";
            req.Timeout = _timeout;
            req.ReadWriteTimeout = _readWriteTimeout;

            return req;
        }

        public static string GetResponseAsString(HttpWebResponse rsp, Encoding encoding)
        {
            using var stream = rsp.ContentEncoding?.Equals(Constants.CONTENT_ENCODING_GZIP, StringComparison.OrdinalIgnoreCase) == true
                ? new GZipStream(rsp.GetResponseStream(), CompressionMode.Decompress) as Stream
                : rsp.GetResponseStream();
            using var reader = new StreamReader(stream, encoding);
            return reader.ReadToEnd();
        }

        public static string BuildRequestUrl(string url, IDictionary<string, string> parameters)
        {
            if (parameters != null && parameters.Count > 0)
                return BuildRequestUrl(url, BuildQuery(parameters));
            return url;
        }

        public static string BuildRequestUrl(string url, params string[] queries)
        {
            if (queries == null || queries.Length == 0) return url;

            var newUrl = new StringBuilder(url);
            bool hasQuery = url.Contains('?');
            bool hasPrepend = url.EndsWith('?') || url.EndsWith('&');

            foreach (string query in queries)
            {
                if (!string.IsNullOrEmpty(query))
                {
                    if (!hasPrepend)
                    {
                        newUrl.Append(hasQuery ? '&' : '?');
                        hasQuery = true;
                    }
                    newUrl.Append(query);
                    hasPrepend = false;
                }
            }
            return newUrl.ToString();
        }

        public static string? BuildQuery(IDictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0) return null;

            var query = new StringBuilder();
            bool hasParam = false;

            foreach (var kv in parameters)
            {
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                {
                    if (hasParam) query.Append('&');
                    query.Append(kv.Key);
                    query.Append('=');
                    query.Append(Uri.EscapeDataString(kv.Value));
                    hasParam = true;
                }
            }
            return query.ToString();
        }

        private static Encoding GetResponseEncoding(HttpWebResponse rsp)
        {
            string? charset = rsp.CharacterSet;
            if (string.IsNullOrEmpty(charset)) charset = Constants.CHARSET_UTF8;
            return Encoding.GetEncoding(charset);
        }
    }
}
