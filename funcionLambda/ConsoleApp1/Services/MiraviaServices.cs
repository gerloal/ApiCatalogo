using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using FuncionLambda.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda.Services
{
    /// <summary>
    /// Servicio que encapsula las operaciones con la Miravia Open Platform:
    ///   - UpdatePricesAndStockAsync: actualiza precios y stock vía API
    ///   - ExportOrdersAsync: descarga pedidos y genera CSVs en S3
    /// </summary>
    public class MiraviaServices
    {
        private const string ApiPathPriceUpdate = "/v2/product/price/update";
        private const string ApiPathStockUpdate  = "/v2/product/quantity/update";
        // Código de almacén de Sportandem en Miravia (obtenido de /rc/warehouse/get)
        private const string WarehouseCode = "dropshipping";
        private const string ApiPathOrdersGet = "/v2/orders/get";
        private const string ApiPathOrderItemsGet = "/v2/order/items/get";
        private const int BatchSize = 20;
        private const int OrdersPageSize = 50;

        private readonly MiraviaApiClient _client;
        private readonly ILambdaContext _ctx;

        public MiraviaServices(MiraviaSecret secret, ILambdaContext ctx)
        {
            _client = new MiraviaApiClient(secret.AppKey, secret.AppSecret, secret.AccessToken);
            _ctx = ctx;
        }

        // ═══════════════════════════════════════════════════════════════
        // FEED CATALOG: actualizar precios y stock
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Actualiza precios y stock en Miravia para la lista de items del CSV.
        /// Procesa en batches de 20 SKUs.
        /// Devuelve un resumen con éxitos y errores de cada operación.
        /// </summary>
        public async Task<MiraviaFeedResult> UpdatePricesAndStockAsync(List<ClientItem> items)
        {
            var result = new MiraviaFeedResult();

            var batches = items
                .Select((item, i) => new { item, i })
                .GroupBy(x => x.i / BatchSize)
                .Select(g => g.Select(x => x.item).ToList())
                .ToList();

            _ctx.Logger.LogLine($"[Miravia] Actualizando {items.Count} items en {batches.Count} batches de {BatchSize}");

            // ── Precios ────────────────────────────────────────────────
            foreach (var batch in batches)
            {
                try
                {
                    var skuPrices = batch
                        .Where(i => i.Price > 0)
                        .Select(i => new
                        {
                            seller_sku = i.Sku,
                            price      = Math.Round(i.Price, 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            sale_price = Math.Round(i.Price, 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                        })
                        .ToList();

                    if (skuPrices.Count == 0) continue;

                    await _client.PostJsonPayloadAsync(ApiPathPriceUpdate, skuPrices);

                    result.PricesUpdated += skuPrices.Count;
                    _ctx.Logger.LogLine($"[Miravia] Precios OK: {skuPrices.Count} SKUs");
                }
                catch (MiraviaApiException ex)
                {
                    result.PriceErrors++;
                    result.Errors.Add($"Precio batch error: {ex.Message}");
                    _ctx.Logger.LogLine($"[Miravia] ERROR precios: {ex.Message}");
                }
            }

            // ── Stocks ─────────────────────────────────────────────────
            foreach (var batch in batches)
            {
                try
                {
                    var skuStocks = batch
                        .Where(i => i.Stock.HasValue)
                        .Select(i => new
                        {
                            seller_sku         = i.Sku,
                            warehouse_quantity = new[]
                            {
                                new { warehouse_code = WarehouseCode, quantity = i.Stock!.Value.ToString() }
                            }
                        })
                        .ToList();

                    if (skuStocks.Count == 0) continue;

                    await _client.PostJsonPayloadAsync(ApiPathStockUpdate, skuStocks);

                    result.StocksUpdated += skuStocks.Count;
                    _ctx.Logger.LogLine($"[Miravia] Stocks OK: {skuStocks.Count} SKUs");
                }
                catch (MiraviaApiException ex)
                {
                    result.StockErrors++;
                    result.Errors.Add($"Stock batch error: {ex.Message}");
                    _ctx.Logger.LogLine($"[Miravia] ERROR stocks: {ex.Message}");
                }
            }

            _ctx.Logger.LogLine($"[Miravia] Feed completado. Precios: {result.PricesUpdated} OK / {result.PriceErrors} errores. " +
                                $"Stocks: {result.StocksUpdated} OK / {result.StockErrors} errores.");
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // EXPORT ORDERS: descargar pedidos y generar CSVs
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Descarga todos los pedidos de Miravia en el rango de fechas dado,
        /// genera dos CSVs (cabeceras y líneas) y los sube a S3.
        /// Actualiza el job en DynamoDB al finalizar.
        /// </summary>
        public async Task<MiraviaExportResult> ExportOrdersAsync(
            string tenantId,
            string jobId,
            DateTime startDate,
            DateTime endDate,
            string s3Bucket,
            IAmazonS3 s3Client,
            IAmazonDynamoDB ddbClient,
            string ddbTable)
        {
            _ctx.Logger.LogLine($"[Miravia] Exportando pedidos {startDate:yyyy-MM-dd} → {endDate:yyyy-MM-dd}");

            var (headers, lines) = await GetAllOrdersAsync(startDate, endDate);

            _ctx.Logger.LogLine($"[Miravia] {headers.Count} pedidos, {lines.Count} líneas");

            var headersKey = $"exports/{tenantId}/{jobId}_miravia_headers.csv";
            var linesKey = $"exports/{tenantId}/{jobId}_miravia_lines.csv";

            await UploadCsvAsync(s3Client, s3Bucket, headersKey, BuildHeadersCsv(headers));
            await UploadCsvAsync(s3Client, s3Bucket, linesKey, BuildLinesCsv(lines));

            var headersUrl = await GeneratePresignedUrlAsync(s3Client, s3Bucket, headersKey, 7);
            var linesUrl = await GeneratePresignedUrlAsync(s3Client, s3Bucket, linesKey, 7);

            var result = new MiraviaExportResult
            {
                JobId = jobId,
                TenantId = tenantId,
                TotalOrders = headers.Count,
                TotalLines = lines.Count,
                HeadersKey = headersKey,
                LinesKey = linesKey,
                HeadersPresignedUrl = headersUrl,
                LinesPresignedUrl = linesUrl
            };

            await UpdateDdbJobDoneAsync(ddbClient, ddbTable, tenantId, jobId, result);

            return result;
        }

        // ───────────── Obtención de pedidos paginada ──────────────────

        private async Task<(List<MiraviaOrder> headers, List<MiraviaOrderItem> lines)> GetAllOrdersAsync(
            DateTime startDate, DateTime endDate)
        {
            var allOrders = new List<MiraviaOrder>();
            var allItems = new List<MiraviaOrderItem>();
            int offset = 0;

            while (true)
            {
                var apiParams = new Dictionary<string, string>
                {
                    ["created_after"] = startDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["created_before"] = endDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["offset"] = offset.ToString(),
                    ["limit"] = OrdersPageSize.ToString(),
                    ["sort_by"] = "created_at",
                    ["sort_direction"] = "ASC"
                };

                var data = await _client.GetAsync<MiraviaOrdersData>(ApiPathOrdersGet, apiParams);
                if (data?.Orders == null || data.Orders.Length == 0) break;

                allOrders.AddRange(data.Orders);

                // Obtener ítems de cada pedido
                foreach (var order in data.Orders)
                {
                    var itemsData = await _client.GetAsync<MiraviaOrderItemsData>(
                        ApiPathOrderItemsGet,
                        new Dictionary<string, string> { ["order_id_list"] = $"[{order.OrderId}]" });

                    if (itemsData?.OrderItems != null)
                        allItems.AddRange(itemsData.OrderItems);

                    await Task.Delay(200); // rate limiting
                }

                if (data.Orders.Length < OrdersPageSize) break;
                offset += OrdersPageSize;
            }

            return (allOrders, allItems);
        }

        // ───────────── Generación de CSV ──────────────────────────────

        private static string BuildHeadersCsv(List<MiraviaOrder> orders)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,CreatedAt,UpdatedAt,Status,Price,Currency,ShippingFee,PaymentMethod,BuyerName,Address,City,PostCode,Country,Phone");

            foreach (var o in orders)
            {
                var name = $"{o.AddressInfo?.FirstName} {o.AddressInfo?.LastName}".Trim();
                sb.AppendLine(string.Join(",",
                    Csv(o.OrderId.ToString()),
                    Csv(o.CreatedAt),
                    Csv(o.UpdatedAt),
                    Csv(o.Status),
                    Csv(o.Price),
                    Csv(o.Currency),
                    Csv(o.ShippingFee),
                    Csv(o.PaymentMethod),
                    Csv(name),
                    Csv(o.AddressInfo?.Address ?? ""),
                    Csv(o.AddressInfo?.City ?? ""),
                    Csv(o.AddressInfo?.PostCode ?? ""),
                    Csv(o.AddressInfo?.Country ?? ""),
                    Csv(o.AddressInfo?.Phone ?? "")));
            }

            return sb.ToString();
        }

        private static string BuildLinesCsv(List<MiraviaOrderItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,OrderItemId,Sku,ShopSku,Name,Variation,Quantity,PaidPrice,ItemPrice,Currency,ShippingFee,TaxAmount,Status");

            foreach (var i in items)
            {
                sb.AppendLine(string.Join(",",
                    Csv(i.OrderId.ToString()),
                    Csv(i.OrderItemId.ToString()),
                    Csv(i.Sku),
                    Csv(i.ShopSku),
                    Csv(i.Name),
                    Csv(i.Variation),
                    Csv(i.Quantity.ToString()),
                    Csv(i.PaidPrice),
                    Csv(i.ItemPrice),
                    Csv(i.Currency),
                    Csv(i.ShippingFee),
                    Csv(i.TaxAmount),
                    Csv(i.Status)));
            }

            return sb.ToString();
        }

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        // ───────────── S3 ──────────────────────────────────────────────

        private static async Task UploadCsvAsync(IAmazonS3 s3, string bucket, string key, string content)
        {
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ContentBody = content,
                ContentType = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            await s3.PutObjectAsync(request);
        }

        private static async Task<string> GeneratePresignedUrlAsync(IAmazonS3 s3, string bucket, string key, int days)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = key,
                Expires = DateTime.UtcNow.AddDays(days),
                Verb = HttpVerb.GET
            };
            return await Task.FromResult(s3.GetPreSignedURL(request));
        }

        // ───────────── DynamoDB ────────────────────────────────────────

        private static async Task UpdateDdbJobDoneAsync(
            IAmazonDynamoDB ddb, string table, string tenantId, string jobId, MiraviaExportResult result)
        {
            var request = new UpdateItemRequest
            {
                TableName = table,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["pk"] = new AttributeValue { S = $"TENANT#{tenantId}" },
                    ["sk"] = new AttributeValue { S = $"JOB#{jobId}" }
                },
                UpdateExpression = "SET #s = :status, updatedAt = :now, totalOrders = :orders, totalLines = :lines, " +
                                   "headersFileKey = :hKey, linesFileKey = :lKey, headersUrl = :hUrl, linesUrl = :lUrl",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":status"] = new AttributeValue { S = "DONE" },
                    [":now"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                    [":orders"] = new AttributeValue { N = result.TotalOrders.ToString() },
                    [":lines"] = new AttributeValue { N = result.TotalLines.ToString() },
                    [":hKey"] = new AttributeValue { S = result.HeadersKey },
                    [":lKey"] = new AttributeValue { S = result.LinesKey },
                    [":hUrl"] = new AttributeValue { S = result.HeadersPresignedUrl },
                    [":lUrl"] = new AttributeValue { S = result.LinesPresignedUrl }
                }
            };
            await ddb.UpdateItemAsync(request);
        }
    }

    // ─── DTOs de resultado ──────────────────────────────────────────────

    public class MiraviaFeedResult
    {
        public int PricesUpdated { get; set; }
        public int PriceErrors { get; set; }
        public int StocksUpdated { get; set; }
        public int StockErrors { get; set; }
        public List<string> Errors { get; set; } = new();

        public string ToSummary() =>
            $"Miravia Feed: precios actualizados={PricesUpdated} (errores={PriceErrors}), " +
            $"stocks actualizados={StocksUpdated} (errores={StockErrors})";
    }

    public class MiraviaExportResult
    {
        public string JobId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public int TotalOrders { get; set; }
        public int TotalLines { get; set; }
        public string HeadersKey { get; set; } = string.Empty;
        public string LinesKey { get; set; } = string.Empty;
        public string HeadersPresignedUrl { get; set; } = string.Empty;
        public string LinesPresignedUrl { get; set; } = string.Empty;
    }
}
