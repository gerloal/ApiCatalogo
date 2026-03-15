using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using FikaAmazonAPI;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Feeds;
using FikaAmazonAPI.AmazonSpApiSDK.Models.ProductPricing;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Sellers;
using FuncionLambda.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    public class CatalogoService
    {
        private const string FEED_CATALOG = "FEED_CATALOG";
        private const string EXPORT_ORDERS = "EXPORT_ORDERS";
        private const string MIRAVIA_FEED_CATALOG = "MIRAVIA_FEED_CATALOG";
        private const string MIRAVIA_EXPORT_ORDERS = "MIRAVIA_EXPORT_ORDERS";
        private const string PCCOMPONENTES_FEED_CATALOG  = "PCCOMPONENTES_FEED_CATALOG";
        private const string PCCOMPONENTES_EXPORT_ORDERS = "PCCOMPONENTES_EXPORT_ORDERS";

        public static async Task DownloadAsync(IAmazonS3 s3, string bucket, string key, string path)
        {
            try
            {
                using var obj = await s3.GetObjectAsync(bucket, key);

                using var fs = File.Create(path);

                await obj.ResponseStream.CopyToAsync(fs);
            }
            catch (Exception x)
            {
                Console.WriteLine(x);
            }
        }
        public static async Task<List<ClientItem>> TransformCatalogFromToListItems(IAmazonS3 s3, string bucket, string key, ILambdaContext ctx)
        {
            try
            {

                var items = new List<ClientItem>();

                using var obj = await s3.GetObjectAsync(bucket, key);

                using var reader = new StreamReader(obj.ResponseStream);

                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {

                    var parts = line.Split(';');

                    if (parts.Length < 3) continue; // Ignora líneas mal formateadas

                    var sku = parts[0].Trim();

                    var stockParsed = int.TryParse(parts[1], out var stock) ? stock : (int?)null;

                    var priceParsed = decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m;

                    items.Add(new ClientItem
                    {
                        Sku = sku,
                        Stock = stockParsed,
                        Price = priceParsed
                    });

                    ctx.Logger.LogLine($"Item procesado: {sku}, Stock: {stockParsed}, Price: {priceParsed}");
                }

                return items;
            }
            catch (Exception x)
            {
                Console.WriteLine(x);
                return new List<ClientItem>();
            }
        }


        // Intercambio LWA refresh_token -> access_token


            // Aquí pones tu lógica SP-API (crear feed document, subir, crear feed, etc.)
        public static async Task ProcessFeedAsync(string filePath, string contentType, string lwaAccessToken, string tenantId)
        {
            // TODO:
            // 1) POST /feeds/2021-06-30/documents -> get upload URL + encryption params
            // 2) PUT a la URL prefirmada con el archivo
            // 3) POST /feeds/2021-06-30/feeds con el documentId y marketplaces
            await Task.CompletedTask;
        }

        public static async Task MarkAsNewJobReceived(IAmazonDynamoDB ddb, string table, string tenantId, string jobId, string bucket, string key, string operation)
        {
            try
            {
                var client = new AmazonDynamoDBClient(RegionEndpoint.EUWest1);

                var request = new PutItemRequest
                {
                    TableName = table,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["pk"] = new AttributeValue { S = $"TENANT#{tenantId}" }, // 👈 OBLIGATORIO
                        ["sk"] = new AttributeValue { S = $"JOB#{jobId}" },     // 👈 OBLIGATORIO
                        ["jobId"] = new AttributeValue { S = jobId },
                        ["tenantId"] = new AttributeValue { S = tenantId },
                        ["status"] = new AttributeValue { S = "PENDING" },
                        ["bucket"] = new AttributeValue { S = bucket },
                        ["key"] = new AttributeValue { S = key },
                        ["operation"] = new AttributeValue { S = operation },
                        ["contentType"] = new AttributeValue { S = "application/json" },
                        ["createdAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    },
                    // Opcional: evita sobreescribir si ya existe ese jobId para ese tenant
                    ConditionExpression = "attribute_not_exists(pk) AND attribute_not_exists(sk)"
                };             

                await client.PutItemAsync(request);


            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al marcar nuevo job: {ex.Message}");
            }
        }
        // Idempotencia con condición en DDB
        public static async Task MarkJobProcessingAsync(IAmazonDynamoDB ddb, string table, string jobId, string tenantId, string estado = "PROCESSING")
        {
            try
            {
                var client = new AmazonDynamoDBClient(RegionEndpoint.EUWest1);

                // 👇 Debes saber los valores exactos de pk y sk del job a actualizar
                var pk = $"TENANT#{tenantId}";
                var sk = $"JOB#{jobId}";

                var request = new UpdateItemRequest
                {
                    TableName = table,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["pk"] = new AttributeValue { S = pk },
                        ["sk"] = new AttributeValue { S = sk }
                    },
                    UpdateExpression = "SET #s = :newStatus, updatedAt = :now",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#s"] = "status"
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":newStatus"] = new AttributeValue { S = estado },
                        [":now"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                    },
                    ReturnValues = "ALL_NEW"
                };

                var response = await client.UpdateItemAsync(request);
                Console.WriteLine("Item actualizado:");
                foreach (var attr in response.Attributes)
                {
                    Console.WriteLine($"{attr.Key}: {attr.Value.S ?? attr.Value.N}");
                }
            }
            catch (Exception)
            {
                // Si ya estaba PROCESSING o DONE, dejamos continuar o abortamos según tu política
                // Aquí simplemente continuamos.
            }

        }

        public static async Task UpdateJobAsync(IAmazonDynamoDB ddb, string table, string jobId, string status, string msg)
        {
            try
            {
                var req = new UpdateItemRequest
                {
                    TableName = table,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "jobId", new AttributeValue { S = jobId } }
                    },
                    UpdateExpression = "SET #s = :s, lastMessage = :m, updatedAt = :now",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        { "#s", "status" }
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":s",   new AttributeValue { S = status } },
                        { ":m",   new AttributeValue { S = msg?.Substring(0, Math.Min(500, msg.Length)) ?? string.Empty } },
                        { ":now", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                    }
                };

                await ddb.UpdateItemAsync(req);
            }
            catch (Exception)
            {
                // Si ya estaba PROCESSING o DONE, dejamos continuar o abortamos según tu política
                // Aquí simplemente continuamos.
            }

        }

        public static async Task ProcessClientOperation(string operation, string tenantId, IAmazonS3 s3, string bucket, string key, string env, string project, ILambdaContext ctx)
        {
            string feedIDStocks = string.Empty;
            string feedIDPrices = string.Empty;
            string reportResultStocks = string.Empty;
            string reportResultPrices = string.Empty;

            try
            {
                ctx.Logger.LogLine($"Procesando mensaje");

                if (operation.Equals(FEED_CATALOG, StringComparison.OrdinalIgnoreCase))
                {

                    SecretManagerService _secretManagerService = new SecretManagerService(new Amazon.SecretsManager.AmazonSecretsManagerClient());

                    SpApiSecret secret = await _secretManagerService.GetSpApiSecretAsync(tenantId, env, project, ctx);

                    AmazonConnection amazonConnection = new AmazonConnection(new AmazonCredential()
                    {
                        ClientId = secret.ClientId,
                        ClientSecret = secret.ClientSecret,
                        RefreshToken = secret.RefreshToken,
                        MarketPlaceID = secret.MarketPlaceID,
                        RoleArn = secret.RoleArn,
                        SellerID = secret.SellerId,
                        IsDebugMode = true
                    }, null);


                    var items = await TransformCatalogFromToListItems(s3, bucket, key, ctx);

                    ctx.Logger.LogLine($"Items leídos: {items.Count}");

                    
                    AmazonServices amazonServices = new AmazonServices(amazonConnection, ctx);

                    /**************************************************************************************/
                    /**** PRICING ****/

                    feedIDPrices = await amazonServices.SubmitFeedPRICING_JSONAsync(items);

                    await amazonServices.GetJsonFeedDetails(amazonConnection, feedIDPrices, tenantId, secret.ClientEmail, secret.ClientPartnerEmail, "Precios");

                    ctx.Logger.LogLine($"Completed: {operation} - PRICING - {tenantId}");
            

                    /**************************************************************************************/
                    /**** STOCKS ****/

                    feedIDStocks = await amazonServices.SubmitInventoryJSON_Async(items);

                    await amazonServices.GetJsonFeedDetails(amazonConnection, feedIDStocks, tenantId, secret.ClientEmail, secret.ClientPartnerEmail, "Stocks");

                    ctx.Logger.LogLine($"Completed: {operation} - STOCK UPDATE - {tenantId}");

                    /**************************************************************************************/

                }
                else if (operation.Equals(EXPORT_ORDERS, StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessOrdersExportAsync(tenantId, s3, bucket, key, env, project, ctx);
                }
                else if (operation.Equals(MIRAVIA_FEED_CATALOG, StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessMiraviaFeedCatalogAsync(tenantId, s3, bucket, key, env, project, ctx);
                }
                else if (operation.Equals(MIRAVIA_EXPORT_ORDERS, StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessMiraviaExportOrdersAsync(tenantId, s3, bucket, key, env, project, ctx);
                }
                else if (operation.Equals(PCCOMPONENTES_FEED_CATALOG, StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessPcComponentesFeedCatalogAsync(tenantId, s3, bucket, key, env, project, ctx);
                }
                else if (operation.Equals(PCCOMPONENTES_EXPORT_ORDERS, StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessPcComponentesExportOrdersAsync(tenantId, s3, bucket, key, env, project, ctx);
                }
            }
            catch (Exception x)
            {
                ctx.Logger.LogLine($"ProcessClientOperation ERROR: {x.Message}");
            }

        }


        public static async Task ProcessOrdersExportAsync(
            string tenantId,
            IAmazonS3 s3,
            string bucket,
            string key,
            string env,
            string project,
            ILambdaContext ctx)
        {
            try
            {
                ctx.Logger.LogLine($"Iniciando exportación de pedidos para {tenantId}");

                SecretManagerService secretManagerService = new SecretManagerService(new Amazon.SecretsManager.AmazonSecretsManagerClient());
                SpApiSecret secret = await secretManagerService.GetSpApiSecretAsync(tenantId, env, project, ctx);

                AmazonConnection amazonConnection = new AmazonConnection(new AmazonCredential()
                {
                    ClientId = secret.ClientId,
                    ClientSecret = secret.ClientSecret,
                    RefreshToken = secret.RefreshToken,
                    MarketPlaceID = secret.MarketPlaceID,
                    RoleArn = secret.RoleArn,
                    SellerID = secret.SellerId,
                    IsDebugMode = true
                }, null);

                DateTime endDate = DateTime.UtcNow;
                DateTime startDate = endDate.AddDays(-30);

                string jobId = Guid.NewGuid().ToString();

                OrderExportService orderExportService = new OrderExportService(ctx);

                var ddbClient = new AmazonDynamoDBClient(RegionEndpoint.EUWest1);
                string tableName = "catalog-api-dev-jobs";

                await MarkAsNewJobReceived(ddbClient, tableName, tenantId, jobId, bucket, key, EXPORT_ORDERS);
                await MarkJobProcessingAsync(ddbClient, tableName, jobId, tenantId, "PROCESSING");

                var result = await orderExportService.ExportOrdersAsync(
                    amazonConnection,
                    tenantId,
                    jobId,
                    startDate,
                    endDate,
                    bucket,
                    s3,
                    ddbClient,
                    tableName);

                ctx.Logger.LogLine($"Exportación completada: {result.TotalOrders} pedidos, {result.TotalLines} líneas");
                ctx.Logger.LogLine($"URL Cabeceras: {result.HeadersPresignedUrl}");
                ctx.Logger.LogLine($"URL Líneas: {result.LinesPresignedUrl}");
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"Error en ProcessOrdersExportAsync: {ex.Message}");
                throw;
            }
        }

        public static async Task ProcessMiraviaFeedCatalogAsync(
            string tenantId,
            IAmazonS3 s3,
            string bucket,
            string key,
            string env,
            string project,
            ILambdaContext ctx)
        {
            try
            {
                ctx.Logger.LogLine($"[Miravia] Iniciando FEED_CATALOG para {tenantId}");

                var secretManagerService = new SecretManagerService(new Amazon.SecretsManager.AmazonSecretsManagerClient());
                var secret = await secretManagerService.GetMiraviaSecretAsync(tenantId, env, project, ctx);

                if (secret == null)
                    throw new InvalidOperationException($"No se encontraron credenciales Miravia para tenant={tenantId}");

                var items = await TransformCatalogFromToListItems(s3, bucket, key, ctx);
                ctx.Logger.LogLine($"[Miravia] Items leídos del CSV: {items.Count}");

                var miraviaServices = new MiraviaServices(secret, ctx);
                var result = await miraviaServices.UpdatePricesAndStockAsync(items);

                ctx.Logger.LogLine($"[Miravia] {result.ToSummary()}");

                if (result.Errors.Count > 0)
                    ctx.Logger.LogLine($"[Miravia] Errores: {string.Join("; ", result.Errors)}");
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"[Miravia] ERROR en ProcessMiraviaFeedCatalogAsync: {ex.Message}");
                throw;
            }
        }

        public static async Task ProcessMiraviaExportOrdersAsync(
            string tenantId,
            IAmazonS3 s3,
            string bucket,
            string key,
            string env,
            string project,
            ILambdaContext ctx)
        {
            try
            {
                ctx.Logger.LogLine($"[Miravia] Iniciando EXPORT_ORDERS para {tenantId}");

                var secretManagerService = new SecretManagerService(new Amazon.SecretsManager.AmazonSecretsManagerClient());
                var secret = await secretManagerService.GetMiraviaSecretAsync(tenantId, env, project, ctx);

                if (secret == null)
                    throw new InvalidOperationException($"No se encontraron credenciales Miravia para tenant={tenantId}");

                var jobId = Guid.NewGuid().ToString();
                var ddbClient = new AmazonDynamoDBClient(RegionEndpoint.EUWest1);
                string tableName = "catalog-api-dev-jobs";

                await MarkAsNewJobReceived(ddbClient, tableName, tenantId, jobId, bucket, key, MIRAVIA_EXPORT_ORDERS);
                await MarkJobProcessingAsync(ddbClient, tableName, jobId, tenantId, "PROCESSING");

                var endDate = DateTime.UtcNow;
                var startDate = endDate.AddDays(-30);

                var miraviaServices = new MiraviaServices(secret, ctx);
                var result = await miraviaServices.ExportOrdersAsync(
                    tenantId, jobId, startDate, endDate,
                    bucket, s3, ddbClient, tableName);

                ctx.Logger.LogLine($"[Miravia] Exportación completada: {result.TotalOrders} pedidos, {result.TotalLines} líneas");
                ctx.Logger.LogLine($"[Miravia] URL Cabeceras: {result.HeadersPresignedUrl}");
                ctx.Logger.LogLine($"[Miravia] URL Líneas: {result.LinesPresignedUrl}");
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"[Miravia] ERROR en ProcessMiraviaExportOrdersAsync: {ex.Message}");
                throw;
            }
        }

        public static async Task ProcessPcComponentesFeedCatalogAsync(
            string tenantId,
            IAmazonS3 s3,
            string bucket,
            string key,
            string env,
            string project,
            ILambdaContext ctx)
        {
            try
            {
                ctx.Logger.LogLine($"[PcComponentes] Iniciando FEED_CATALOG para {tenantId}");

                var secretManagerService = new SecretManagerService(new Amazon.SecretsManager.AmazonSecretsManagerClient());
                var secret = await secretManagerService.GetPcComponentesSecretAsync(tenantId, env, project, ctx);

                if (secret == null)
                    throw new InvalidOperationException($"No se encontraron credenciales PcComponentes para tenant={tenantId}");

                var items = await TransformCatalogFromToListItems(s3, bucket, key, ctx);
                ctx.Logger.LogLine($"[PcComponentes] Items leídos del CSV: {items.Count}");

                var pcSvc  = new FuncionLambda.Services.PcComponentesServices(secret, ctx);
                var result = await pcSvc.UpdateCatalogAsync(items);

                ctx.Logger.LogLine($"[PcComponentes] {result.ToSummary()}");

                if (result.Errors.Count > 0)
                    ctx.Logger.LogLine($"[PcComponentes] Errores: {string.Join("; ", result.Errors)}");
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"[PcComponentes] ERROR en ProcessPcComponentesFeedCatalogAsync: {ex.Message}");
                throw;
            }
        }

        public static async Task ProcessPcComponentesExportOrdersAsync(
            string tenantId,
            IAmazonS3 s3,
            string bucket,
            string key,
            string env,
            string project,
            ILambdaContext ctx)
        {
            try
            {
                ctx.Logger.LogLine($"[PcComponentes] Iniciando EXPORT_ORDERS para {tenantId}");

                var secretManagerService = new SecretManagerService(new Amazon.SecretsManager.AmazonSecretsManagerClient());
                var secret = await secretManagerService.GetPcComponentesSecretAsync(tenantId, env, project, ctx);

                if (secret == null)
                    throw new InvalidOperationException($"No se encontraron credenciales PcComponentes para tenant={tenantId}");

                var jobId     = Guid.NewGuid().ToString();
                var ddbClient = new AmazonDynamoDBClient(RegionEndpoint.EUWest1);
                string tableName = "catalog-api-dev-jobs";

                await MarkAsNewJobReceived(ddbClient, tableName, tenantId, jobId, bucket, key, PCCOMPONENTES_EXPORT_ORDERS);
                await MarkJobProcessingAsync(ddbClient, tableName, jobId, tenantId, "PROCESSING");

                var endDate   = DateTime.UtcNow;
                var startDate = endDate.AddDays(-30);

                var pcSvc  = new FuncionLambda.Services.PcComponentesServices(secret, ctx);
                var result = await pcSvc.ExportOrdersAsync(
                    tenantId, jobId, startDate, endDate,
                    bucket, s3, ddbClient, tableName);

                ctx.Logger.LogLine($"[PcComponentes] Exportación completada: {result.TotalOrders} pedidos, {result.TotalLines} líneas");
                ctx.Logger.LogLine($"[PcComponentes] URL Cabeceras: {result.HeadersPresignedUrl}");
                ctx.Logger.LogLine($"[PcComponentes] URL Líneas: {result.LinesPresignedUrl}");
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"[PcComponentes] ERROR en ProcessPcComponentesExportOrdersAsync: {ex.Message}");
                throw;
            }
        }

        static string ExtractJobId(string body)
        {
            try
            {
                var m = JsonSerializer.Deserialize<QueueMsg>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return m?.jobId ?? "unknown";
            }
            catch { return "unknown"; }
        }
    }
}
