using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using FikaAmazonAPI;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Orders;
using FikaAmazonAPI.Parameter.Order;
using FuncionLambda.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FuncionLambda.Services
{
    public class OrderExportService
    {
        private const string EXPORT_BUCKET_PREFIX = "exports";
        private readonly ILambdaContext _ctx;

        public OrderExportService(ILambdaContext ctx)
        {
            _ctx = ctx;
        }

        /// <summary>
        /// Obtiene pedidos de Amazon en un rango de fechas y genera archivos CSV
        /// </summary>
        public async Task<OrderExportResult> ExportOrdersAsync(
            AmazonConnection amazonConnection,
            string tenantId,
            string jobId,
            DateTime startDate,
            DateTime endDate,
            string bucket,
            IAmazonS3 s3Client,
            IAmazonDynamoDB ddbClient,
            string tableName)
        {
            var result = new OrderExportResult
            {
                JobId = jobId,
                TenantId = tenantId,
                Status = "PROCESSING"
            };

            try
            {
                _ctx.Logger.LogLine($"Iniciando exportación de pedidos para {tenantId} desde {startDate:yyyy-MM-dd} hasta {endDate:yyyy-MM-dd}");

                // 1. Obtener pedidos de Amazon SP-API
                var (headers, lines) = await GetOrdersFromAmazonAsync(amazonConnection, startDate, endDate);

                _ctx.Logger.LogLine($"Obtenidos {headers.Count} pedidos con {lines.Count} líneas");

                var isSportandem = string.Equals(tenantId, "Sportandem", StringComparison.OrdinalIgnoreCase);

                // 2. Generar archivos CSV
                var headersKey = $"{EXPORT_BUCKET_PREFIX}/{tenantId}/{jobId}_headers.csv";
                var linesKey = $"{EXPORT_BUCKET_PREFIX}/{tenantId}/{jobId}_lines.csv";

                var headersContent = isSportandem ? GenerateSportandemHeadersFile(headers) : GenerateHeadersCsv(headers);
                var linesContent = isSportandem ? GenerateSportandemLinesFile(lines) : GenerateLinesCsv(lines);

                await UploadCsvToS3Async(s3Client, bucket, headersKey, headersContent);
                await UploadCsvToS3Async(s3Client, bucket, linesKey, linesContent);

                _ctx.Logger.LogLine($"Archivos CSV subidos a S3: {headersKey}, {linesKey}");

                // 3. Generar URLs pre-firmadas (válidas por 7 días)
                var headersUrl = await GeneratePresignedUrlAsync(s3Client, bucket, headersKey, 7);
                var linesUrl = await GeneratePresignedUrlAsync(s3Client, bucket, linesKey, 7);

                // 4. Actualizar resultado
                result.HeadersFileKey = headersKey;
                result.LinesFileKey = linesKey;
                result.HeadersPresignedUrl = headersUrl;
                result.LinesPresignedUrl = linesUrl;
                result.TotalOrders = headers.Count;
                result.TotalLines = lines.Count;
                result.Status = "DONE";

                // 5. Actualizar DynamoDB con el resultado
                await UpdateJobWithExportResultAsync(ddbClient, tableName, tenantId, jobId, result);

                _ctx.Logger.LogLine($"Exportación completada exitosamente para job {jobId}");

                return result;
            }
            catch (Exception ex)
            {
                _ctx.Logger.LogLine($"Error en exportación de pedidos: {ex.Message}");
                result.Status = "FAILED";
                
                await UpdateJobWithExportResultAsync(ddbClient, tableName, tenantId, jobId, result, ex.Message);
                
                throw;
            }
        }

        /// <summary>
        /// Obtiene pedidos de Amazon SP-API
        /// </summary>
        private async Task<(List<OrderHeader> headers, List<OrderLine> lines)> GetOrdersFromAmazonAsync(
            AmazonConnection amazonConnection,
            DateTime startDate,
            DateTime endDate)
        {
            var headers = new List<OrderHeader>();
            var lines = new List<OrderLine>();

            try
            {
                var marketplaceIds = new List<string> { amazonConnection.GetCurrentMarketplace.ID };
                
                var searchOrderList = new ParameterOrderList()
                {
                    CreatedAfter = startDate,
                    CreatedBefore = endDate,
                    MarketplaceIds = marketplaceIds,
                    MaxResultsPerPage = 100
                };

                var ordersResponse = await amazonConnection.Orders.GetOrdersAsync(searchOrderList);

                if (ordersResponse == null || ordersResponse.Count == 0)
                {
                    _ctx.Logger.LogLine("No se encontraron pedidos en el rango de fechas especificado");
                    return (headers, lines);
                }

                // Procesar cada pedido
                foreach (var order in ordersResponse)
                {
                    var header = new OrderHeader
                    {
                        AmazonOrderId = order.AmazonOrderId,
                        PurchaseDate = order.PurchaseDate,
                        OrderStatus = order.OrderStatus.ToString(),
                        OrderTotal = decimal.TryParse(order.OrderTotal?.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var total) ? total : 0,
                        Currency = order.OrderTotal?.CurrencyCode,
                        BuyerEmail = order.BuyerInfo?.BuyerEmail ?? "",
                        BuyerName = order.BuyerInfo?.BuyerName ?? "",
                        ShipServiceLevel = order.ShipmentServiceLevelCategory,
                        ShipmentServiceLevelCategory = order.ShipmentServiceLevelCategory,
                        ShippingAddress = order.ShippingAddress?.AddressLine1 ?? "",
                        City = order.ShippingAddress?.City ?? "",
                        StateOrRegion = order.ShippingAddress?.StateOrRegion ?? "",
                        PostalCode = order.ShippingAddress?.PostalCode ?? "",
                        CountryCode = order.ShippingAddress?.CountryCode ?? ""
                    };
                    headers.Add(header);

                    // Obtener líneas del pedido
                    try
                    {
                        var orderItems = await amazonConnection.Orders.GetOrderItemsAsync(order.AmazonOrderId);
                        
                        if (orderItems != null && orderItems.Count > 0)
                        {
                            foreach (var item in orderItems)
                            {
                                var line = new OrderLine
                                {
                                    AmazonOrderId = order.AmazonOrderId,
                                    OrderItemId = item.OrderItemId,
                                    ASIN = item.ASIN,
                                    SellerSKU = item.SellerSKU,
                                    Title = item.Title,
                                    QuantityOrdered = item.QuantityOrdered ?? 0,
                                    QuantityShipped = item.QuantityShipped ?? 0,
                                    ItemPrice = decimal.TryParse(item.ItemPrice?.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var itemPrice) ? itemPrice : 0,
                                    Currency = item.ItemPrice?.CurrencyCode ?? "",
                                    ItemTax = decimal.TryParse(item.ItemTax?.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var itemTax) ? itemTax : 0,
                                    ShippingPrice = decimal.TryParse(item.ShippingPrice?.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var shippingPrice) ? shippingPrice : 0,
                                    ShippingTax = decimal.TryParse(item.ShippingTax?.Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var shippingTax) ? shippingTax : 0
                                };
                                lines.Add(line);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _ctx.Logger.LogLine($"Error obteniendo items del pedido {order.AmazonOrderId}: {ex.Message}");
                    }

                    await Task.Delay(2000);
                }

                return (headers, lines);
            }
            catch (Exception ex)
            {
                _ctx.Logger.LogLine($"Error obteniendo pedidos de Amazon: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Genera CSV para cabeceras de pedidos
        /// </summary>
        private string GenerateHeadersCsv(List<OrderHeader> headers)
        {
            var sb = new StringBuilder();
            
            // Encabezado
            sb.AppendLine("AmazonOrderId,PurchaseDate,OrderStatus,OrderTotal,Currency,BuyerEmail,BuyerName,ShipServiceLevel,ShippingAddress,City,StateOrRegion,PostalCode,CountryCode");

            // Datos
            foreach (var header in headers)
            {
                sb.AppendLine($"{EscapeCsv(header.AmazonOrderId)}," +
                    $"{EscapeCsv(header.PurchaseDate)}," +
                    $"{EscapeCsv(header.OrderStatus)}," +
                    $"{header.OrderTotal.ToString(CultureInfo.InvariantCulture)}," +
                    $"{EscapeCsv(header.Currency)}," +
                    $"{EscapeCsv(header.BuyerEmail)}," +
                    $"{EscapeCsv(header.BuyerName)}," +
                    $"{EscapeCsv(header.ShipServiceLevel)}," +
                    $"{EscapeCsv(header.ShippingAddress)}," +
                    $"{EscapeCsv(header.City)}," +
                    $"{EscapeCsv(header.StateOrRegion)}," +
                    $"{EscapeCsv(header.PostalCode)}," +
                    $"{EscapeCsv(header.CountryCode)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Genera CSV para líneas de pedidos
        /// </summary>
        private string GenerateLinesCsv(List<OrderLine> lines)
        {
            var sb = new StringBuilder();
            
            // Encabezado
            sb.AppendLine("AmazonOrderId,OrderItemId,ASIN,SellerSKU,Title,QuantityOrdered,QuantityShipped,ItemPrice,Currency,ItemTax,ShippingPrice,ShippingTax");

            // Datos
            foreach (var line in lines)
            {
                sb.AppendLine($"{EscapeCsv(line.AmazonOrderId)}," +
                    $"{EscapeCsv(line.OrderItemId)}," +
                    $"{EscapeCsv(line.ASIN)}," +
                    $"{EscapeCsv(line.SellerSKU)}," +
                    $"{EscapeCsv(line.Title)}," +
                    $"{line.QuantityOrdered}," +
                    $"{line.QuantityShipped}," +
                    $"{line.ItemPrice.ToString(CultureInfo.InvariantCulture)}," +
                    $"{EscapeCsv(line.Currency)}," +
                    $"{line.ItemTax.ToString(CultureInfo.InvariantCulture)}," +
                    $"{line.ShippingPrice.ToString(CultureInfo.InvariantCulture)}," +
                    $"{line.ShippingTax.ToString(CultureInfo.InvariantCulture)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Escapa valores para CSV (maneja comillas y comas)
        /// </summary>
        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        /// <summary>
        /// Sube contenido CSV a S3
        /// </summary>
        private async Task UploadCsvToS3Async(IAmazonS3 s3Client, string bucket, string key, string csvContent)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
            
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                ContentType = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };

            await s3Client.PutObjectAsync(request);
        }

        /// <summary>
        /// Genera URL pre-firmada para descargar archivo
        /// </summary>
        private async Task<string> GeneratePresignedUrlAsync(IAmazonS3 s3Client, string bucket, string key, int expirationDays)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = key,
                Expires = DateTime.UtcNow.AddDays(expirationDays)
            };

            return await Task.FromResult(s3Client.GetPreSignedURL(request));
        }

        /// <summary>
        /// Actualiza DynamoDB con el resultado de la exportación
        /// </summary>
        private async Task UpdateJobWithExportResultAsync(
            IAmazonDynamoDB ddbClient,
            string tableName,
            string tenantId,
            string jobId,
            OrderExportResult result,
            string errorMessage = null)
        {
            var pk = $"TENANT#{tenantId}";
            var sk = $"JOB#{jobId}";

            var updateExpression = "SET #s = :status, updatedAt = :now, totalOrders = :orders, totalLines = :lines";
            var attributeNames = new Dictionary<string, string>
            {
                ["#s"] = "status"
            };
            var attributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new AttributeValue { S = result.Status },
                [":now"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") },
                [":orders"] = new AttributeValue { N = result.TotalOrders.ToString() },
                [":lines"] = new AttributeValue { N = result.TotalLines.ToString() }
            };

            if (!string.IsNullOrEmpty(result.HeadersFileKey))
            {
                updateExpression += ", headersFileKey = :headersKey, headersUrl = :headersUrl";
                attributeValues[":headersKey"] = new AttributeValue { S = result.HeadersFileKey };
                attributeValues[":headersUrl"] = new AttributeValue { S = result.HeadersPresignedUrl };
            }

            if (!string.IsNullOrEmpty(result.LinesFileKey))
            {
                updateExpression += ", linesFileKey = :linesKey, linesUrl = :linesUrl";
                attributeValues[":linesKey"] = new AttributeValue { S = result.LinesFileKey };
                attributeValues[":linesUrl"] = new AttributeValue { S = result.LinesPresignedUrl };
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                updateExpression += ", errorMessage = :error";
                attributeValues[":error"] = new AttributeValue { S = errorMessage.Substring(0, Math.Min(500, errorMessage.Length)) };
            }

            var request = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["pk"] = new AttributeValue { S = pk },
                    ["sk"] = new AttributeValue { S = sk }
                },
                UpdateExpression = updateExpression,
                ExpressionAttributeNames = attributeNames,
                ExpressionAttributeValues = attributeValues
            };

            await ddbClient.UpdateItemAsync(request);
        }

        private string GenerateSportandemHeadersFile(List<OrderHeader> headers)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildFixedWidthRow(
                ("AmazonOrderId", 23),
                ("PurchaseDate", 24),
                ("OrderStatus", 14),
                ("OrderTotal", 12),
                ("Currency", 10),
                ("BuyerEmail", 45),
                ("BuyerName", 11),
                ("ShipServiceLevel", 20),
                ("ShippingAddress", 18),
                ("City", 50),
                ("StateOrRegion", 18),
                ("PostalCode", 12),
                ("CountryCode", 14)));

            foreach (var header in headers)
            {
                sb.AppendLine(BuildFixedWidthRow(
                    (header.AmazonOrderId, 23),
                    (header.PurchaseDate, 24),
                    (header.OrderStatus, 14),
                    (header.OrderTotal.ToString(CultureInfo.InvariantCulture), 12),
                    (header.Currency, 10),
                    (header.BuyerEmail, 45),
                    (header.BuyerName, 11),
                    (header.ShipServiceLevel, 20),
                    (header.ShippingAddress, 18),
                    (header.City, 50),
                    (header.StateOrRegion, 18),
                    (header.PostalCode, 12),
                    (header.CountryCode, 14)));
            }

            return sb.ToString();
        }

        private string GenerateSportandemLinesFile(List<OrderLine> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildFixedWidthRow(
                ("AmazonOrderId", 23),
                ("OrderItemId", 17),
                ("ASIN", 12),
                ("SellerSKU", 48),
                ("Title", 240),
                ("QuantityOrdered", 18),
                ("QuantityShipped", 18),
                ("ItemPrice", 11),
                ("Currency", 10),
                ("ItemTax", 9),
                ("ShippingPrice", 16),
                ("ShippingTax", 14)));

            foreach (var line in lines)
            {
                sb.AppendLine(BuildFixedWidthRow(
                    (line.AmazonOrderId, 23),
                    (line.OrderItemId, 17),
                    (line.ASIN, 12),
                    (line.SellerSKU, 48),
                    (line.Title, 240),
                    (line.QuantityOrdered.ToString(CultureInfo.InvariantCulture), 18),
                    (line.QuantityShipped.ToString(CultureInfo.InvariantCulture), 18),
                    (line.ItemPrice.ToString(CultureInfo.InvariantCulture), 11),
                    (line.Currency, 10),
                    (line.ItemTax.ToString(CultureInfo.InvariantCulture), 9),
                    (line.ShippingPrice.ToString(CultureInfo.InvariantCulture), 16),
                    (line.ShippingTax.ToString(CultureInfo.InvariantCulture), 14)));
            }

            return sb.ToString();
        }

        private string BuildFixedWidthRow(params (string value, int length)[] fields)
        {
            var sb = new StringBuilder();
            foreach (var field in fields)
            {
                sb.Append(PadOrTrim(field.value, field.length));
            }

            return sb.ToString();
        }

        private string PadOrTrim(string value, int length)
        {
            var safeValue = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

            if (safeValue.Length > length)
            {
                return safeValue[..length];
            }

            return safeValue.PadRight(length);
        }
    }
}
