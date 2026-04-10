using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using FuncionLambda.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FuncionLambda.Services
{
    /// <summary>
    /// Servicio AliExpress Open Platform (IOP).
    /// Misma estructura que MiraviaServices — endpoints AliExpress.
    /// </summary>
    public class AliExpressServices
    {
        private const string ApiPathPriceUpdate  = "/aliexpress/solution/batch/product/price/update";
        private const string ApiPathStockUpdate  = "/aliexpress/solution/batch/product/inventory/update";
        private const string ApiPathOrdersGet    = "/aliexpress/trade/redefining/findorders";
        private const string ApiPathOrderItemsGet = "/aliexpress/trade/redefining/getorderbyid";
        private const int BatchSize      = 20;
        private const int OrdersPageSize = 50;

        private readonly AliExpressApiClient _client;
        private readonly ILambdaContext _ctx;

        public AliExpressServices(AliExpressSecret secret, ILambdaContext ctx)
        {
            _client = new AliExpressApiClient(secret.AppKey, secret.AppSecret, secret.AccessToken);
            _ctx    = ctx;
        }

        internal AliExpressServices(AliExpressApiClient client, ILambdaContext ctx)
        {
            _client = client;
            _ctx    = ctx;
        }

        // ═══════════════════════════════════════════════════════════════
        // FEED CATALOG: precios y stock
        // ═══════════════════════════════════════════════════════════════

        public async Task<AliExpressFeedResult> UpdatePricesAndStockAsync(List<ClientItem> items)
        {
            var result  = new AliExpressFeedResult();
            var batches = items
                .Select((item, i) => new { item, i })
                .GroupBy(x => x.i / BatchSize)
                .Select(g => g.Select(x => x.item).ToList())
                .ToList();

            _ctx.Logger.LogLine($"[AliExpress] Actualizando {items.Count} items en {batches.Count} batches");

            foreach (var batch in batches)
            {
                // Precios
                try
                {
                    var skuPrices = batch
                        .Where(i => i.Price > 0)
                        .Select(i => new { sku_id = i.Sku, sale_price = Math.Round(i.Price, 2).ToString("F2") })
                        .ToList();

                    if (skuPrices.Count > 0)
                    {
                        await _client.PostJsonPayloadAsync(ApiPathPriceUpdate, new { skus = skuPrices });
                        result.PricesUpdated += skuPrices.Count;
                        _ctx.Logger.LogLine($"[AliExpress] Precios OK: {skuPrices.Count} SKUs");
                    }
                }
                catch (AliExpressApiException ex)
                {
                    result.PriceErrors++;
                    result.Errors.Add($"AliExpress precio error: {ex.Message}");
                    _ctx.Logger.LogLine($"[AliExpress] ERROR precios: {ex.Message}");
                }

                // Stocks
                try
                {
                    var skuStocks = batch
                        .Where(i => i.Stock.HasValue)
                        .Select(i => new { sku_id = i.Sku, quantity = i.Stock!.Value })
                        .ToList();

                    if (skuStocks.Count > 0)
                    {
                        await _client.PostJsonPayloadAsync(ApiPathStockUpdate, new { skus = skuStocks });
                        result.StocksUpdated += skuStocks.Count;
                        _ctx.Logger.LogLine($"[AliExpress] Stocks OK: {skuStocks.Count} SKUs");
                    }
                }
                catch (AliExpressApiException ex)
                {
                    result.StockErrors++;
                    result.Errors.Add($"AliExpress stock error: {ex.Message}");
                    _ctx.Logger.LogLine($"[AliExpress] ERROR stocks: {ex.Message}");
                }
            }

            _ctx.Logger.LogLine($"[AliExpress] Feed completado. {result.ToSummary()}");
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // EXPORT ORDERS
        // ═══════════════════════════════════════════════════════════════

        public async Task<AliExpressExportResult> ExportOrdersAsync(
            string tenantId,
            string jobId,
            DateTime startDate,
            DateTime endDate,
            string s3Bucket,
            IAmazonS3 s3Client,
            IAmazonDynamoDB ddbClient,
            string ddbTable)
        {
            _ctx.Logger.LogLine($"[AliExpress] Exportando pedidos {startDate:yyyy-MM-dd} → {endDate:yyyy-MM-dd}");

            var (headers, lines) = await GetAllOrdersAsync(startDate, endDate);

            _ctx.Logger.LogLine($"[AliExpress] {headers.Count} pedidos, {lines.Count} líneas");

            var headersKey = $"exports/{tenantId}/{jobId}_aliexpress_headers.csv";
            var linesKey   = $"exports/{tenantId}/{jobId}_aliexpress_lines.csv";

            await UploadCsvAsync(s3Client, s3Bucket, headersKey, BuildHeadersCsv(headers));
            await UploadCsvAsync(s3Client, s3Bucket, linesKey,   BuildLinesCsv(lines));

            var headersUrl = await GeneratePresignedUrlAsync(s3Client, s3Bucket, headersKey, 7);
            var linesUrl   = await GeneratePresignedUrlAsync(s3Client, s3Bucket, linesKey,   7);

            var result = new AliExpressExportResult
            {
                JobId               = jobId,
                TenantId            = tenantId,
                TotalOrders         = headers.Count,
                TotalLines          = lines.Count,
                HeadersKey          = headersKey,
                LinesKey            = linesKey,
                HeadersPresignedUrl = headersUrl,
                LinesPresignedUrl   = linesUrl
            };

            await UpdateDdbJobDoneAsync(ddbClient, ddbTable, tenantId, jobId, result);
            return result;
        }

        private async Task<(List<AliExpressOrder> headers, List<AliExpressOrderItem> lines)> GetAllOrdersAsync(
            DateTime startDate, DateTime endDate)
        {
            var allOrders = new List<AliExpressOrder>();
            var allItems  = new List<AliExpressOrderItem>();
            int page      = 1;

            while (true)
            {
                var apiParams = new Dictionary<string, string>
                {
                    ["create_date_start"] = startDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["create_date_end"]   = endDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["page_index"]        = page.ToString(),
                    ["page_size"]         = OrdersPageSize.ToString()
                };

                var data = await _client.GetAsync<AliExpressOrdersData>(ApiPathOrdersGet, apiParams);
                if (data?.Orders == null || data.Orders.Length == 0) break;

                allOrders.AddRange(data.Orders);

                foreach (var order in data.Orders)
                {
                    var itemsData = await _client.GetAsync<AliExpressOrderItemsData>(
                        ApiPathOrderItemsGet,
                        new Dictionary<string, string> { ["order_id"] = order.OrderId.ToString() });

                    if (itemsData?.OrderItems != null)
                        allItems.AddRange(itemsData.OrderItems);

                    await Task.Delay(200);
                }

                if (data.Orders.Length < OrdersPageSize) break;
                page++;
            }

            return (allOrders, allItems);
        }

        private static string BuildHeadersCsv(List<AliExpressOrder> orders)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,CreatedAt,UpdatedAt,Status,Price,Currency,LogisticsAmount,BuyerName,Address,City,PostCode,Country,Phone");

            foreach (var o in orders)
            {
                sb.AppendLine(string.Join(",",
                    Csv(o.OrderId.ToString()),
                    Csv(o.CreatedAt),
                    Csv(o.UpdatedAt),
                    Csv(o.Status),
                    Csv(o.TotalPrice),
                    Csv(o.Currency),
                    Csv(o.LogisticsAmount),
                    Csv(o.BuyerName),
                    Csv(o.ReceiptAddress?.Address ?? ""),
                    Csv(o.ReceiptAddress?.City ?? ""),
                    Csv(o.ReceiptAddress?.Zip ?? ""),
                    Csv(o.ReceiptAddress?.Country ?? ""),
                    Csv(o.ReceiptAddress?.Mobile ?? "")));
            }
            return sb.ToString();
        }

        private static string BuildLinesCsv(List<AliExpressOrderItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,OrderItemId,ProductId,SkuCode,Name,Quantity,UnitPrice,TotalPrice,Currency,Status");

            foreach (var i in items)
            {
                sb.AppendLine(string.Join(",",
                    Csv(i.OrderId.ToString()),
                    Csv(i.OrderItemId.ToString()),
                    Csv(i.ProductId.ToString()),
                    Csv(i.SkuCode),
                    Csv(i.ProductName),
                    Csv(i.Quantity.ToString()),
                    Csv(i.UnitPrice),
                    Csv(i.TotalPrice),
                    Csv(i.Currency),
                    Csv(i.Status)));
            }
            return sb.ToString();
        }

        private static string Csv(string? v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return $"\"{v.Replace("\"", "\"\"")}\"";
            return v;
        }

        private static async Task UploadCsvAsync(IAmazonS3 s3, string bucket, string key, string content)
        {
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName                = bucket,
                Key                       = key,
                ContentBody               = content,
                ContentType               = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            });
        }

        private static async Task<string> GeneratePresignedUrlAsync(IAmazonS3 s3, string bucket, string key, int days)
            => await Task.FromResult(s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key        = key,
                Expires    = DateTime.UtcNow.AddDays(days),
                Verb       = HttpVerb.GET
            }));

        private static async Task UpdateDdbJobDoneAsync(
            IAmazonDynamoDB ddb, string table, string tenantId, string jobId, AliExpressExportResult result)
        {
            await ddb.UpdateItemAsync(new UpdateItemRequest
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
                    [":now"]    = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                    [":orders"] = new AttributeValue { N = result.TotalOrders.ToString() },
                    [":lines"]  = new AttributeValue { N = result.TotalLines.ToString() },
                    [":hKey"]   = new AttributeValue { S = result.HeadersKey },
                    [":lKey"]   = new AttributeValue { S = result.LinesKey },
                    [":hUrl"]   = new AttributeValue { S = result.HeadersPresignedUrl },
                    [":lUrl"]   = new AttributeValue { S = result.LinesPresignedUrl }
                }
            });
        }
    }

    // ─── Modelos AliExpress ────────────────────────────────────────────

    public class AliExpressOrder
    {
        [JsonPropertyName("order_id")]       public long   OrderId         { get; set; }
        [JsonPropertyName("created_at")]     public string CreatedAt        { get; set; } = string.Empty;
        [JsonPropertyName("updated_at")]     public string UpdatedAt        { get; set; } = string.Empty;
        [JsonPropertyName("status")]         public string Status            { get; set; } = string.Empty;
        [JsonPropertyName("total_price")]    public string TotalPrice        { get; set; } = "0";
        [JsonPropertyName("currency_code")]  public string Currency          { get; set; } = string.Empty;
        [JsonPropertyName("logistics_amount")] public string LogisticsAmount { get; set; } = "0";
        [JsonPropertyName("buyer_login_id")] public string BuyerName         { get; set; } = string.Empty;
        [JsonPropertyName("receipt_address")] public AliExpressAddress? ReceiptAddress { get; set; }
    }

    public class AliExpressAddress
    {
        [JsonPropertyName("name")]           public string Name    { get; set; } = string.Empty;
        [JsonPropertyName("address")]        public string Address { get; set; } = string.Empty;
        [JsonPropertyName("city")]           public string City    { get; set; } = string.Empty;
        [JsonPropertyName("zip")]            public string Zip     { get; set; } = string.Empty;
        [JsonPropertyName("country")]        public string Country { get; set; } = string.Empty;
        [JsonPropertyName("mobile_no")]      public string Mobile  { get; set; } = string.Empty;
    }

    public class AliExpressOrderItem
    {
        [JsonPropertyName("order_item_id")]  public long   OrderItemId  { get; set; }
        [JsonPropertyName("order_id")]       public long   OrderId      { get; set; }
        [JsonPropertyName("product_id")]     public long   ProductId    { get; set; }
        [JsonPropertyName("sku_code")]       public string SkuCode      { get; set; } = string.Empty;
        [JsonPropertyName("product_name")]   public string ProductName  { get; set; } = string.Empty;
        [JsonPropertyName("quantity")]       public int    Quantity     { get; set; }
        [JsonPropertyName("unit_price")]     public string UnitPrice    { get; set; } = "0";
        [JsonPropertyName("total_price")]    public string TotalPrice   { get; set; } = "0";
        [JsonPropertyName("currency_code")]  public string Currency     { get; set; } = string.Empty;
        [JsonPropertyName("status")]         public string Status       { get; set; } = string.Empty;
    }

    public class AliExpressOrdersData
    {
        [JsonPropertyName("total_count")]    public int Count { get; set; }
        [JsonPropertyName("order_list")]     public AliExpressOrder[] Orders { get; set; } = [];
    }

    public class AliExpressOrderItemsData
    {
        [JsonPropertyName("order")]          public AliExpressOrderItem[] OrderItems { get; set; } = [];
    }

    // ─── DTOs de resultado ─────────────────────────────────────────────

    public class AliExpressFeedResult
    {
        public int PricesUpdated { get; set; }
        public int PriceErrors   { get; set; }
        public int StocksUpdated { get; set; }
        public int StockErrors   { get; set; }
        public List<string> Errors { get; set; } = new();

        public string ToSummary() =>
            $"AliExpress Feed: precios={PricesUpdated} OK/{PriceErrors} err, stocks={StocksUpdated} OK/{StockErrors} err";
    }

    public class AliExpressExportResult
    {
        public string JobId               { get; set; } = string.Empty;
        public string TenantId            { get; set; } = string.Empty;
        public int    TotalOrders         { get; set; }
        public int    TotalLines          { get; set; }
        public string HeadersKey          { get; set; } = string.Empty;
        public string LinesKey            { get; set; } = string.Empty;
        public string HeadersPresignedUrl { get; set; } = string.Empty;
        public string LinesPresignedUrl   { get; set; } = string.Empty;
    }
}
