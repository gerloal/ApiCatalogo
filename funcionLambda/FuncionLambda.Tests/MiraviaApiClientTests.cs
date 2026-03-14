using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FuncionLambda;
using Moq;
using Moq.Protected;
using Xunit;

namespace FuncionLambda.Tests
{
    public class MiraviaApiClientTests
    {
        private const string AppKey    = "test_app_key";
        private const string AppSecret = "test_app_secret";
        private const string Token     = "test_access_token";

        // ── Firma ────────────────────────────────────────────────────

        [Fact]
        public void ComputeSign_ParametersOrderedAlphabetically()
        {
            var client = new MiraviaApiClient(AppKey, AppSecret, Token);
            var apiPath = "/orders/get";

            // Parámetros desordenados a propósito
            var paramsDesorden = new Dictionary<string, string>
            {
                ["z_param"] = "zzz",
                ["a_param"] = "aaa",
                ["m_param"] = "mmm"
            };

            // La firma debe ser la misma independientemente del orden de inserción
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
            var client = new MiraviaApiClient(AppKey, AppSecret, Token);
            var sign = client.ComputeSign("/product/price/update", new Dictionary<string, string>
            {
                ["app_key"] = AppKey,
                ["timestamp"] = "1700000000000"
            });

            sign.Should().MatchRegex("^[0-9A-F]+$");
        }

        [Fact]
        public void ComputeSign_IncludesApiPathAsPrefix()
        {
            var client = new MiraviaApiClient(AppKey, AppSecret, Token);
            var parms = new Dictionary<string, string> { ["x"] = "1" };

            var signPath1 = client.ComputeSign("/orders/get", parms);
            var signPath2 = client.ComputeSign("/product/price/update", parms);

            // Paths distintos → firmas distintas
            signPath1.Should().NotBe(signPath2);
        }

        [Fact]
        public void ComputeSign_KnownVector()
        {
            // Vector de prueba calculado manualmente para verificar la implementación
            var client = new MiraviaApiClient("mykey", "mysecret", "mytoken");
            var apiPath = "/orders/get";
            var parms = new Dictionary<string, string>
            {
                ["app_key"]   = "mykey",
                ["timestamp"] = "1700000000000"
            };

            // Cálculo esperado: HMAC-SHA256("mysecret", "/orders/getapp_keymykeytimestamp1700000000000")
            var msg = "/orders/getapp_keymykeytimestamp1700000000000";
            var expected = ComputeHmacSha256("mysecret", msg);

            client.ComputeSign(apiPath, parms).Should().Be(expected);
        }

        // ── ParseResponse ────────────────────────────────────────────

        [Fact]
        public void ParseResponse_ReturnsData_WhenCodeIs0()
        {
            var json = "{\"code\":\"0\",\"data\":{\"count\":5,\"orders\":[]}}";
            var result = MiraviaApiClient.ParseResponse<MiraviaOrdersWrapper>(json, "/orders/get");
            result.Should().NotBeNull();
            result!.Count.Should().Be(5);
        }

        [Fact]
        public void ParseResponse_ThrowsMiraviaApiException_WhenCodeIsNot0()
        {
            var json = "{\"code\":\"50\",\"message\":\"Invalid access token\"}";
            var act = () => MiraviaApiClient.ParseResponse<object>(json, "/orders/get");
            act.Should().Throw<MiraviaApiException>()
               .Which.ErrorCode.Should().Be("50");
        }

        [Fact]
        public void ParseResponse_ReturnsMiraviaApiResponse_ForResponseType()
        {
            var json = "{\"code\":\"0\",\"request_id\":\"abc123\"}";
            var result = MiraviaApiClient.ParseResponse<MiraviaApiResponse>(json, "/product/price/update");
            result.Should().NotBeNull();
            result!.Code.Should().Be("0");
        }

        // ── BuildQueryString ─────────────────────────────────────────

        [Fact]
        public void BuildQueryString_EncodesSpecialChars()
        {
            var parms = new Dictionary<string, string> { ["key"] = "val ue&more" };
            var qs = MiraviaApiClient.BuildQueryString(parms);
            var encoded = qs;
            (encoded.Contains("val+ue%26more") || encoded.Contains("val%20ue%26more")).Should().BeTrue("la URL debe codificar el espacio y el &");
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
                    Content = new StringContent("{\"code\":\"0\",\"data\":{\"count\":0,\"orders\":[]}}")
                });
            });
            var httpClient = new HttpClient(handler);

            var client = new MiraviaApiClient(AppKey, AppSecret, Token,
                useSandbox: true, httpClient: httpClient);

            await client.GetAsync<MiraviaOrdersWrapper>("/orders/get",
                new Dictionary<string, string> { ["created_after"] = "2025-01-01 00:00:00" });

            capturedUrl.Should().StartWith("https://api.miravia.es/rest/mock/orders/get");
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
                    Content = new StringContent("{\"code\":\"0\",\"data\":{\"count\":0,\"orders\":[]}}")
                });
            });
            var httpClient = new HttpClient(handler);

            var client = new MiraviaApiClient(AppKey, AppSecret, Token,
                useSandbox: false, httpClient: httpClient);

            await client.GetAsync<MiraviaOrdersWrapper>("/orders/get",
                new Dictionary<string, string> { ["created_after"] = "2025-01-01 00:00:00" });

            capturedUrl.Should().StartWith("https://api.miravia.es/rest/orders/get");
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

    // DTO mínimo para deserializar respuesta de /orders/get en tests
    internal class MiraviaOrdersWrapper
    {
        public int Count { get; set; }
    }

    // Handler HTTP mock reutilizable
    internal class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _handler(request, ct);
    }
}
