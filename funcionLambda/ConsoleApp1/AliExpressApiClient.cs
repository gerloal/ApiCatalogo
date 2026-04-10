using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    /// <summary>
    /// Cliente HTTP para la AliExpress Open Platform API (IOP).
    /// Usa la misma firma HMAC-SHA256 que Miravia (ambas son plataformas IOP).
    /// Endpoint: https://api-sg.aliexpress.com/rest
    /// </summary>
    public class AliExpressApiClient
    {
        private const string ProductionBaseUrl = "https://api-sg.aliexpress.com/rest";
        private const string SandboxBaseUrl    = "https://api-sg.aliexpress.com/rest/mock";

        private readonly HttpClient _http;
        private readonly string _appKey;
        private readonly string _appSecret;
        private readonly string _accessToken;
        private readonly string _baseUrl;

        public AliExpressApiClient(string appKey, string appSecret, string accessToken,
            bool useSandbox = false, HttpClient? httpClient = null)
        {
            _appKey      = appKey;
            _appSecret   = appSecret;
            _accessToken = accessToken;
            _baseUrl     = useSandbox ? SandboxBaseUrl : ProductionBaseUrl;
            _http        = httpClient ?? new HttpClient();
        }

        public async Task<T?> GetAsync<T>(string apiPath, Dictionary<string, string> apiParams)
        {
            var allParams = BuildCommonParams();
            foreach (var kv in apiParams)
                allParams[kv.Key] = kv.Value;

            allParams["sign"] = ComputeSign(apiPath, allParams);

            var url = $"{_baseUrl}{apiPath}?{BuildQueryString(allParams)}";
            var response = await _http.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            return ParseResponse<T>(json, apiPath);
        }

        public async Task<T?> PostAsync<T>(string apiPath, Dictionary<string, string> apiParams)
        {
            var commonParams = BuildCommonParams();

            var allParamsForSign = new Dictionary<string, string>(commonParams);
            foreach (var kv in apiParams)
                allParamsForSign[kv.Key] = kv.Value;

            commonParams["sign"] = ComputeSign(apiPath, allParamsForSign);

            var url  = $"{_baseUrl}{apiPath}?{BuildQueryString(commonParams)}";
            var body = new FormUrlEncodedContent(apiParams);
            var response = await _http.PostAsync(url, body);
            var json = await response.Content.ReadAsStringAsync();
            return ParseResponse<T>(json, apiPath);
        }

        public async Task<AliExpressApiResponse?> PostJsonPayloadAsync(string apiPath, object payload)
        {
            var jsonPayload = JsonSerializer.Serialize(payload);
            var apiParams   = new Dictionary<string, string> { ["payload"] = jsonPayload };
            return await PostAsync<AliExpressApiResponse>(apiPath, apiParams);
        }

        internal string ComputeSign(string apiPath, Dictionary<string, string> allParams)
        {
            var sorted = allParams.OrderBy(kv => kv.Key);
            var sb = new StringBuilder(apiPath);
            foreach (var kv in sorted)
                sb.Append(kv.Key).Append(kv.Value);

            var keyBytes = Encoding.UTF8.GetBytes(_appSecret);
            var msgBytes = Encoding.UTF8.GetBytes(sb.ToString());
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(msgBytes);
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }

        internal static string BuildQueryString(Dictionary<string, string> parameters)
        {
            var parts = parameters.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
            return string.Join("&", parts);
        }

        private Dictionary<string, string> BuildCommonParams() => new()
        {
            ["app_key"]      = _appKey,
            ["timestamp"]    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            ["sign_method"]  = "sha256",
            ["access_token"] = _accessToken
        };

        internal static T? ParseResponse<T>(string json, string apiPath)
        {
            try
            {
                var doc  = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var code = root.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : "unknown";
                if (code != "0")
                {
                    var msg = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : json;
                    throw new AliExpressApiException(apiPath, code ?? "unknown", msg ?? json);
                }

                if (typeof(T) == typeof(AliExpressApiResponse))
                    return (T)(object)new AliExpressApiResponse { Code = code ?? "0", Raw = json };

                if (root.TryGetProperty("data", out var data))
                    return JsonSerializer.Deserialize<T>(data.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return default;
            }
            catch (AliExpressApiException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error al parsear respuesta de {apiPath}: {ex.Message}. JSON: {json}", ex);
            }
        }
    }

    public class AliExpressApiResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Raw  { get; set; } = string.Empty;
    }

    public class AliExpressApiException : Exception
    {
        public string ApiPath   { get; }
        public string ErrorCode { get; }

        public AliExpressApiException(string apiPath, string errorCode, string message)
            : base($"AliExpress API error en {apiPath} [code={errorCode}]: {message}")
        {
            ApiPath   = apiPath;
            ErrorCode = errorCode;
        }
    }
}
