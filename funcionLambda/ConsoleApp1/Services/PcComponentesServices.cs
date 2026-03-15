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
using System.Text;
using System.Threading.Tasks;

namespace FuncionLambda.Services
{
    /// <summary>
    /// Servicio que encapsula las operaciones con la API Mirakl de PcComponentes:
    ///   - UpdateCatalogAsync: actualiza precios y stock vía OF01 (import CSV)
    ///   - ExportOrdersAsync: descarga pedidos paginados y genera CSVs en S3
    ///
    /// API Reference: https://developer.mirakl.com/content/product/mmp/rest/seller/openapi3
    /// </summary>
    public class PcComponentesServices
    {
        private const int ImportPollIntervalMs  = 5_000;  // 5 s entre polls de estado
        private const int ImportMaxPollAttempts = 60;     // hasta 5 min esperando COMPLETE/FAILED
        private const int OrdersPageSize        = 100;    // máximo permitido por OR11

        private readonly PcComponentesApiClient _client;
        private readonly ILambdaContext _ctx;

        public PcComponentesServices(PcComponentesSecret secret, ILambdaContext ctx, bool useSandbox = false)
        {
            _client = new PcComponentesApiClient(secret, useSandbox);
            _ctx    = ctx;
        }

        /// <summary>Constructor para inyección en tests.</summary>
        internal PcComponentesServices(PcComponentesApiClient client, ILambdaContext ctx)
        {
            _client = client;
            _ctx    = ctx;
        }

        // ═══════════════════════════════════════════════════════════════════
        // FEED CATALOG — actualizar precios y stock vía OF01 + polling OF02
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Genera un CSV con los items del catálogo y lo importa a PcComponentes mediante OF01.
        /// Hace polling cada 5 s hasta que el import llegue a COMPLETE o FAILED (máx. 5 min).
        /// </summary>
        public async Task<PcComponentesFeedResult> UpdateCatalogAsync(List<ClientItem> items)
        {
            _ctx.Logger.LogLine($"[PcComponentes] Iniciando catalog update: {items.Count} items");

            var csv      = BuildOffersCsv(items);
            var csvBytes = Encoding.UTF8.GetBytes(csv);

            using var stream       = new MemoryStream(csvBytes);
            var       importResult = await _client.ImportOffersAsync(stream);

            if (importResult == null)
                throw new InvalidOperationException("[PcComponentes] ImportOffersAsync devolvió null");

            _ctx.Logger.LogLine($"[PcComponentes] Import iniciado: import_id={importResult.ImportId}");

            var status = await PollImportStatusAsync(importResult.ImportId);

            var result = new PcComponentesFeedResult
            {
                ImportId      = importResult.ImportId,
                ImportStatus  = status.Status,
                LinesRead     = status.LinesRead,
                OffersInserted = status.OfferInserted,
                OffersUpdated  = status.OfferUpdated,
                LinesInError   = status.LinesInError,
                HasErrorReport = status.HasErrorReport
            };

            if (status.Status == "FAILED")
                result.Errors.Add($"Import {importResult.ImportId} FAILED. Lines in error: {status.LinesInError}");

            _ctx.Logger.LogLine($"[PcComponentes] {result.ToSummary()}");
            return result;
        }

        private async Task<MiraklImportStatus> PollImportStatusAsync(long importId)
        {
            for (int attempt = 0; attempt < ImportMaxPollAttempts; attempt++)
            {
                var status = await _client.GetImportStatusAsync(importId);

                if (status == null)
                    throw new InvalidOperationException(
                        $"[PcComponentes] GetImportStatusAsync devolvió null para import {importId}");

                _ctx.Logger.LogLine(
                    $"[PcComponentes] Import {importId} status={status.Status} " +
                    $"(intento {attempt + 1}/{ImportMaxPollAttempts})");

                if (status.Status == "COMPLETE" || status.Status == "FAILED")
                    return status;

                await Task.Delay(ImportPollIntervalMs);
            }

            throw new TimeoutException(
                $"[PcComponentes] Timeout esperando import {importId} tras {ImportMaxPollAttempts} intentos");
        }

        /// <summary>
        /// CSV OF01 con las columnas mínimas que acepta Mirakl: sku;price;quantity.
        /// Separador semicolón, codificación UTF-8.
        /// </summary>
        private static string BuildOffersCsv(List<ClientItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("sku;price;quantity");

            foreach (var item in items)
            {
                var price = item.Price.ToString("F2", CultureInfo.InvariantCulture);
                var qty   = item.Stock.HasValue ? item.Stock.Value.ToString() : string.Empty;
                sb.AppendLine($"{EscapeCsv(item.Sku)};{price};{qty}");
            }

            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        // EXPORT ORDERS — paginación OR11, dos CSVs en S3, DynamoDB actualizado
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Descarga todos los pedidos PcComponentes en el rango de fechas dado,
        /// genera dos CSVs (cabeceras y líneas) y los sube a S3.
        /// Actualiza el job en DynamoDB al finalizar.
        /// </summary>
        public async Task<PcComponentesExportResult> ExportOrdersAsync(
            string tenantId,
            string jobId,
            DateTime startDate,
            DateTime endDate,
            string s3Bucket,
            IAmazonS3 s3Client,
            IAmazonDynamoDB ddbClient,
            string ddbTable)
        {
            _ctx.Logger.LogLine(
                $"[PcComponentes] Exportando pedidos {startDate:yyyy-MM-dd} → {endDate:yyyy-MM-dd}");

            var (orders, lines) = await GetAllOrdersAsync(startDate, endDate);

            _ctx.Logger.LogLine($"[PcComponentes] {orders.Count} pedidos, {lines.Count} líneas");

            var headersKey = $"exports/{tenantId}/{jobId}_pccomponentes_headers.csv";
            var linesKey   = $"exports/{tenantId}/{jobId}_pccomponentes_lines.csv";

            await UploadCsvAsync(s3Client, s3Bucket, headersKey, BuildHeadersCsv(orders));
            await UploadCsvAsync(s3Client, s3Bucket, linesKey,   BuildLinesCsv(lines));

            var headersUrl = GeneratePresignedUrl(s3Client, s3Bucket, headersKey, 7);
            var linesUrl   = GeneratePresignedUrl(s3Client, s3Bucket, linesKey,   7);

            var result = new PcComponentesExportResult
            {
                JobId              = jobId,
                TenantId           = tenantId,
                TotalOrders        = orders.Count,
                TotalLines         = lines.Count,
                HeadersKey         = headersKey,
                LinesKey           = linesKey,
                HeadersPresignedUrl = headersUrl,
                LinesPresignedUrl  = linesUrl
            };

            await UpdateDdbJobDoneAsync(ddbClient, ddbTable, tenantId, jobId, result);
            return result;
        }

        private async Task<(List<MiraklOrder> orders, List<MiraklOrderLine> lines)> GetAllOrdersAsync(
            DateTime startDate, DateTime endDate)
        {
            var allOrders = new List<MiraklOrder>();
            var allLines  = new List<MiraklOrderLine>();
            int offset    = 0;

            while (true)
            {
                var data = await _client.GetOrdersAsync(startDate, endDate, offset, OrdersPageSize);
                if (data?.Orders == null || data.Orders.Length == 0) break;

                foreach (var order in data.Orders)
                {
                    // Denormalizamos el OrderId en cada línea para los CSVs
                    foreach (var line in order.OrderLines)
                        line.ParentOrderId = order.OrderId;

                    allLines.AddRange(order.OrderLines);
                }

                allOrders.AddRange(data.Orders);

                _ctx.Logger.LogLine(
                    $"[PcComponentes] Página obtenida: {data.Orders.Length} pedidos " +
                    $"(offset={offset}, total={data.TotalCount})");

                if (allOrders.Count >= data.TotalCount || data.Orders.Length < OrdersPageSize) break;
                offset += OrdersPageSize;

                await Task.Delay(200); // rate limiting cortesía
            }

            return (allOrders, allLines);
        }

        // ─── Generación de CSV ──────────────────────────────────────────────

        private static string BuildHeadersCsv(List<MiraklOrder> orders)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,CommercialId,CreatedDate,LastUpdatedDate,OrderState," +
                          "TotalPrice,Currency,CustomerName,CustomerEmail," +
                          "BillingCity,BillingCountry,ShippingCity,ShippingCountry,ShippingPrice");

            foreach (var o in orders)
            {
                var customerName = $"{o.Customer?.Firstname} {o.Customer?.Lastname}".Trim();
                sb.AppendLine(string.Join(",",
                    Csv(o.OrderId),
                    Csv(o.CommercialId),
                    Csv(o.CreatedDate),
                    Csv(o.LastUpdatedDate),
                    Csv(o.OrderState),
                    o.TotalPrice.ToString("F2", CultureInfo.InvariantCulture),
                    Csv(o.CurrencyIsoCode),
                    Csv(customerName),
                    Csv(o.Customer?.Email ?? string.Empty),
                    Csv(o.Customer?.BillingAddress?.City ?? string.Empty),
                    Csv(o.Customer?.BillingAddress?.CountryIsoCode ?? string.Empty),
                    Csv(o.Customer?.ShippingAddress?.City ?? string.Empty),
                    Csv(o.Customer?.ShippingAddress?.CountryIsoCode ?? string.Empty),
                    o.Shipping?.Price.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty));
            }

            return sb.ToString();
        }

        private static string BuildLinesCsv(List<MiraklOrderLine> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,OrderLineId,OfferSku,ProductTitle,Quantity,Price,PriceUnit,Currency,OrderLineState");

            foreach (var l in lines)
            {
                sb.AppendLine(string.Join(",",
                    Csv(l.ParentOrderId),
                    Csv(l.OrderLineId),
                    Csv(l.OfferSku),
                    Csv(l.ProductTitle),
                    l.Quantity.ToString(),
                    l.Price.ToString("F2", CultureInfo.InvariantCulture),
                    l.PriceUnit.ToString("F2", CultureInfo.InvariantCulture),
                    Csv(l.CurrencyIsoCode),
                    Csv(l.OrderLineState)));
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

        // ─── Helpers CSV ───────────────────────────────────────────────────

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        // ─── S3 ────────────────────────────────────────────────────────────

        private static async Task UploadCsvAsync(IAmazonS3 s3, string bucket, string key, string content)
        {
            var request = new PutObjectRequest
            {
                BucketName                 = bucket,
                Key                        = key,
                ContentBody                = content,
                ContentType                = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            await s3.PutObjectAsync(request);
        }

        private static string GeneratePresignedUrl(IAmazonS3 s3, string bucket, string key, int days)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key        = key,
                Expires    = DateTime.UtcNow.AddDays(days),
                Verb       = HttpVerb.GET
            };
            return s3.GetPreSignedURL(request);
        }

        // ─── DynamoDB ──────────────────────────────────────────────────────

        private static async Task UpdateDdbJobDoneAsync(
            IAmazonDynamoDB ddb, string table, string tenantId, string jobId,
            PcComponentesExportResult result)
        {
            var request = new UpdateItemRequest
            {
                TableName = table,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["pk"] = new AttributeValue { S = $"TENANT#{tenantId}" },
                    ["sk"] = new AttributeValue { S = $"JOB#{jobId}" }
                },
                UpdateExpression =
                    "SET #s = :status, updatedAt = :now, totalOrders = :orders, totalLines = :lines, " +
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
            };
            await ddb.UpdateItemAsync(request);
        }
    }

    // ─── DTOs de resultado ──────────────────────────────────────────────────

    public class PcComponentesFeedResult
    {
        public long   ImportId       { get; set; }
        public string ImportStatus   { get; set; } = string.Empty;
        public int    LinesRead      { get; set; }
        public int    OffersInserted { get; set; }
        public int    OffersUpdated  { get; set; }
        public int    LinesInError   { get; set; }
        public bool   HasErrorReport { get; set; }
        public List<string> Errors   { get; set; } = new();

        public string ToSummary() =>
            $"PcComponentes Feed: import_id={ImportId} status={ImportStatus} " +
            $"insertadas={OffersInserted} actualizadas={OffersUpdated} errores={LinesInError}";
    }

    public class PcComponentesExportResult
    {
        public string JobId              { get; set; } = string.Empty;
        public string TenantId           { get; set; } = string.Empty;
        public int    TotalOrders        { get; set; }
        public int    TotalLines         { get; set; }
        public string HeadersKey         { get; set; } = string.Empty;
        public string LinesKey           { get; set; } = string.Empty;
        public string HeadersPresignedUrl { get; set; } = string.Empty;
        public string LinesPresignedUrl  { get; set; } = string.Empty;
    }
}
