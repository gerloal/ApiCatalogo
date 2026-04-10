using FuncionLambda.Models;
using Iop.Api;
using Iop.Api.Util;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda.Services
{
    /// <summary>
    /// Integración con Miravia a través del IOP SDK.
    /// Docs: https://open.miravia.es/
    /// </summary>
    public class MiraviaService
    {
        private readonly IopClient _client;
        private readonly string _accessToken;

        public MiraviaService(MiraviaSecret secret)
        {
            if (string.IsNullOrEmpty(secret.AppKey)) throw new ArgumentNullException(nameof(secret.AppKey));
            if (string.IsNullOrEmpty(secret.AppSecret)) throw new ArgumentNullException(nameof(secret.AppSecret));
            if (string.IsNullOrEmpty(secret.AccessToken)) throw new ArgumentNullException(nameof(secret.AccessToken));

            _client = new IopClient(UrlConstants.MIRAVIA_ES_API_URL, secret.AppKey, secret.AppSecret);
            _accessToken = secret.AccessToken;
        }

        // ──────────────────────────────────────────────
        //  PEDIDOS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Obtiene pedidos en un rango de fechas (máx. 50 por llamada).
        /// </summary>
        /// <param name="createdAfter">Fecha inicio UTC (formato: yyyy-MM-dd HH:mm:ss)</param>
        /// <param name="createdBefore">Fecha fin UTC</param>
        /// <param name="status">Estado del pedido: unpaid | pending | ready_to_ship | delivered | returned | shipped | failed | canceled</param>
        public Task<string> GetOrdersAsync(DateTime createdAfter, DateTime createdBefore, string status = "ready_to_ship")
        {
            var req = new IopRequest("/orders/get");
            req.SetHttpMethod(Constants.METHOD_GET);
            req.AddApiParameter("created_after", createdAfter.ToString("yyyy-MM-dd HH:mm:ss"));
            req.AddApiParameter("created_before", createdBefore.ToString("yyyy-MM-dd HH:mm:ss"));
            req.AddApiParameter("status", status);
            req.AddApiParameter("limit", "50");

            var response = _client.Execute(req, _accessToken);
            ThrowIfError(response);
            return Task.FromResult(response.Body!);
        }

        /// <summary>
        /// Obtiene el detalle de un pedido por su ID.
        /// </summary>
        public Task<string> GetOrderDetailAsync(string orderId)
        {
            var req = new IopRequest("/order/get");
            req.SetHttpMethod(Constants.METHOD_GET);
            req.AddApiParameter("order_id", orderId);

            var response = _client.Execute(req, _accessToken);
            ThrowIfError(response);
            return Task.FromResult(response.Body!);
        }

        // ──────────────────────────────────────────────
        //  PRODUCTOS
        // ──────────────────────────────────────────────

        /// <summary>
        /// Sube o actualiza un producto en Miravia.
        /// payload: JSON del producto en formato Miravia (ver docs).
        /// </summary>
        public Task<string> CreateProductAsync(string productPayloadJson)
        {
            var req = new IopRequest("/product/create");
            req.AddApiParameter("payload", productPayloadJson);

            var response = _client.Execute(req, _accessToken);
            ThrowIfError(response);
            return Task.FromResult(response.Body!);
        }

        /// <summary>
        /// Actualiza precio y/o stock de un SKU.
        /// </summary>
        public Task<string> UpdatePriceQuantityAsync(string skuId, decimal price, int quantity)
        {
            var payload = JsonSerializer.Serialize(new
            {
                Request = new[]
                {
                    new
                    {
                        SkuId = skuId,
                        SalePrice = price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        Quantity = quantity.ToString()
                    }
                }
            });

            var req = new IopRequest("/product/price_quantity/update");
            req.AddApiParameter("payload", payload);

            var response = _client.Execute(req, _accessToken);
            ThrowIfError(response);
            return Task.FromResult(response.Body!);
        }

        /// <summary>
        /// Obtiene los productos del seller (paginado).
        /// </summary>
        public Task<string> GetProductsAsync(int offset = 0, int limit = 50)
        {
            var req = new IopRequest("/products/get");
            req.SetHttpMethod(Constants.METHOD_GET);
            req.AddApiParameter("offset", offset.ToString());
            req.AddApiParameter("limit", limit.ToString());
            req.AddApiParameter("filter", "all");

            var response = _client.Execute(req, _accessToken);
            ThrowIfError(response);
            return Task.FromResult(response.Body!);
        }

        // ──────────────────────────────────────────────
        //  HELPERS
        // ──────────────────────────────────────────────

        private static void ThrowIfError(IopResponse response)
        {
            if (response.IsError())
                throw new IopException($"Miravia API error [{response.Code}]: {response.Message} (requestId={response.RequestId})");
        }
    }
}
