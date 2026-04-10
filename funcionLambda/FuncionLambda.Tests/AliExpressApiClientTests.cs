using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FuncionLambda;
using Xunit;

namespace FuncionLambda.Tests
{
    public class AliExpressApiClientTests
    {
        private const string AppKey    = "test_app_key";
        private const string AppSecret = "test_app_secret";
        private const string Token     = "test_access_token";

        // ── Firma ────────────────────────────────────────────────────

        [Fact]
        public void ComputeSign_ParametersOrderedAlphabetically()
        {
            var client = new AliExpressApiClient(AppKey, AppSecret, Token);
            var apiPath = "/aliexpress/trade/redefining/findorders";

            var paramsDesorden = new Dictionary<string, string>
            {
                ["z_param"] = "zzz",
                ["a_param"] = "aaa",
                ["m_param"] = "mmm"
            };

            var paramsOrden = new Dictionary<string, string>
            {
                ["a_param"] = "aaa",
                ["m_param"] = "mmm",
                ["z_param"] = "zzz"
            };

            var sign1 = client.ComputeSign(apiPath, paramsDesorden);
            var sign2 = client.ComputeSign(apiPath, paramsOrden);

            sign1.Should().Be(sign2);
        }

        [Fact]
        public void ComputeSign_ResultIsUppercaseHex()
        {
            var client = new AliExpressApiClient(AppKey, AppSecret, Token);
            var sign = client.ComputeSign("/aliexpress/solution/batch/product/price/update",
                new Dictionary<string, string>
                {
                    ["app_key"]  = AppKey,
                    ["timestamp"] = "1700000000000"
                });

            sign.Should().MatchRegex("^[0-9A-F]+$");
        }

        [Fact]
        public void ComputeSign_IncludesApiPathAsPrefix()
        {
            var client = new AliExpressApiClient(AppKey, AppSecret, Token);
            var parms = new Dictionary<string, string> { ["x"] = "1" };

            var signPath1 = client.ComputeSign("/aliexpress/trade/redefining/findorders", parms);
            var signPath2 = client.ComputeSign("/aliexpress/solution/batch/product/price/update", parms);

            signPath1.Should().NotBe(signPath2);
        }

        [Fact]
        public void ComputeSign_KnownVector()
        {
            var client = new AliExpressApiClient("mykey", "mysecret", "mytoken");
            var apiPath = "/aliexpress/trade/redefining/findorders";
            var parms = new Dictionary<string, string>
            {
                ["app_key"]   = "mykey",
                ["timestamp"] = "1700000000000"
            };

            // HMAC-SHA256("mysecret", "/aliexpress/trade/redefining/findordersapp_keymykeytimestamp1700000000000")
            var msg      = "/aliexpress/trade/redefining/findordersapp_keymykeytimestamp1700000000000";
            var expected = ComputeHmacSha256("mysecret", msg);

            client.ComputeSign(apiPath, parms).Should().Be(expected);
        }

        // ── ParseResponse ────────────────────────────────────────────

        [Fact]
        public void ParseResponse_ReturnsData_WhenCodeIs0()
        {
            var json = "{\"code\":\"0\",\"data\":{\"order_count\":3}}";
            var result = AliExpressApiClient.ParseResponse<AliExpressOrdersWrapper>(json,
                "/aliexpress/trade/redefining/findorders");

            result.Should().NotBeNull();
            result!.OrderCount.Should().Be(3);
        }

        [Fact]
        public void ParseResponse_ThrowsAliExpressApiException_WhenCodeIsNot0()
        {
            var json = "{\"code\":\"50\",\"message\":\"Invalid access token\"}";
            var act  = () => AliExpressApiClient.ParseResponse<object>(json,
                "/aliexpress/trade/redefining/findorders");

            act.Should().Throw<AliExpressApiException>()
               .Which.ErrorCode.Should().Be("50");
        }

        [Fact]
        public void ParseResponse_ReturnsAliExpressApiResponse_ForResponseType()
        {
            var json   = "{\"code\":\"0\",\"request_id\":\"abc123\"}";
            var result = AliExpressApiClient.ParseResponse<AliExpressApiResponse>(json,
                "/aliexpress/solution/batch/product/price/update");

            result.Should().NotBeNull();
            result!.Code.Should().Be("0");
        }

        // ── BuildQueryString ─────────────────────────────────────────

        [Fact]
        public void BuildQueryString_EncodesSpecialChars()
        {
            var parms = new Dictionary<string, string> { ["key"] = "val ue&more" };
            var qs    = AliExpressApiClient.BuildQueryString(parms);

            (qs.Contains("val+ue%26more") || qs.Contains("val%20ue%26more"))
                .Should().BeTrue("la URL debe codificar el espacio y el &");
        }

        // ── Sandbox URL ──────────────────────────────────────────────

        [Fact]
        public async Task GetAsync_UsesSandboxUrl_WhenUseSandboxIsTrue()
        {
            string? capturedUrl = null;
            var handler = new MockHttpMessageHandler((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":\"0\",\"data\":{\"order_count\":0}}")
                });
            });

            var client = new AliExpressApiClient(AppKey, AppSecret, Token,
                useSandbox: true, httpClient: new HttpClient(handler));

            await client.GetAsync<AliExpressOrdersWrapper>(
                "/aliexpress/trade/redefining/findorders",
                new Dictionary<string, string> { ["created_after"] = "2025-01-01 00:00:00" });

            capturedUrl.Should().StartWith("https://api-sg.aliexpress.com/rest/mock/aliexpress/");
        }

        [Fact]
        public async Task GetAsync_UsesProductionUrl_WhenUseSandboxIsFalse()
        {
            string? capturedUrl = null;
            var handler = new MockHttpMessageHandler((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"code\":\"0\",\"data\":{\"order_count\":0}}")
                });
            });

            var client = new AliExpressApiClient(AppKey, AppSecret, Token,
                useSandbox: false, httpClient: new HttpClient(handler));

            await client.GetAsync<AliExpressOrdersWrapper>(
                "/aliexpress/trade/redefining/findorders",
                new Dictionary<string, string> { ["created_after"] = "2025-01-01 00:00:00" });

            capturedUrl.Should().StartWith("https://api-sg.aliexpress.com/rest/aliexpress/");
            capturedUrl.Should().NotContain("/mock/");
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static string ComputeHmacSha256(string secret, string message)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var msgBytes = Encoding.UTF8.GetBytes(message);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(msgBytes);
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }
    }

    // DTO mínimo para deserializar respuesta de /findorders en tests
    internal class AliExpressOrdersWrapper
    {
        public int OrderCount { get; set; }
    }
}
