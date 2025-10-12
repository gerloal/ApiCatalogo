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
using FikaAmazonAPI.AmazonSpApiSDK.Models.ProductPricing;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Sellers;
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

                    await amazonServices.SubmitFeedPRICING_JSONAsync(items);

                    ctx.Logger.LogLine($"Completed: {operation} - PRICING - {tenantId}");

                    await amazonServices.SubmitInventoryJSON_Async(items);

                    ctx.Logger.LogLine($"Completed: {operation} - STOCK UPDATE - {tenantId}");

                    
                }
            }
            catch (Exception)
            {
                // Si ya estaba PROCESSING o DONE, dejamos continuar o abortamos según tu política
                // Aquí simplemente continuamos.
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
