using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SecretsManager;
using FikaAmazonAPI;
using FikaAmazonAPI.Parameter;
using FuncionLambda.Models;
using FuncionLambda.Services;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    /// <summary>
    /// Handler unificado que procesa múltiples tipos de mensajes SQS
    /// </summary>
    public class UnifiedSQSHandler
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonDynamoDB _ddbClient;
        private readonly IAmazonSecretsManager _secretsManager;

        public UnifiedSQSHandler()
        {
            _s3Client = new AmazonS3Client();
            _ddbClient = new AmazonDynamoDBClient();
            _secretsManager = new AmazonSecretsManagerClient();
        }

        public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing {sqsEvent.Records.Count} messages");

            foreach (var record in sqsEvent.Records)
            {
                try
                {
                    // Detectar tipo de mensaje
                    var messageType = DetectMessageType(record.Body);
                    context.Logger.LogLine($"Detected message type: {messageType}");

                    switch (messageType)
                    {
                        case MessageType.CatalogOperation:
                            await ProcessCatalogMessageAsync(record, context);
                            break;

                        case MessageType.OrderExport:
                            await ProcessOrderExportMessageAsync(record, context);
                            break;

                        default:
                            context.Logger.LogLine($"Unknown message type, skipping: {record.Body}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Error processing message {record.MessageId}: {ex.Message}");
                    context.Logger.LogLine($"StackTrace: {ex.StackTrace}");
                    throw;
                }
            }
        }

        private MessageType DetectMessageType(string messageBody)
        {
            try
            {
                // Intentar parsear como QueueMsg (catálogo)
                var catalogMsg = JsonSerializer.Deserialize<QueueMsg>(messageBody);
                if (!string.IsNullOrEmpty(catalogMsg?.operation))
                {
                    return MessageType.CatalogOperation;
                }
            }
            catch { }

            try
            {
                // Intentar parsear como ExportOrdersQueueMessage
                var exportMsg = JsonSerializer.Deserialize<ExportOrdersQueueMessage>(messageBody);
                if (!string.IsNullOrEmpty(exportMsg?.Operation) && exportMsg.Operation == "EXPORT_ORDERS")
                {
                    return MessageType.OrderExport;
                }
            }
            catch { }

            return MessageType.Unknown;
        }

        private async Task ProcessCatalogMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
        {
            context.Logger.LogLine("Processing catalog operation message...");
            
            var queueMsg = JsonSerializer.Deserialize<QueueMsg>(message.Body);
            
            // Aquí iría tu lógica existente de Program.cs
            // Por ejemplo:
            // await CatalogoService.ProcessFileAsync(...);
            
            context.Logger.LogLine($"Catalog operation {queueMsg.operation} completed for job {queueMsg.jobId}");
        }

        private async Task ProcessOrderExportMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
        {
            context.Logger.LogLine("Processing order export message...");
            
            var queueMessage = JsonSerializer.Deserialize<ExportOrdersQueueMessage>(message.Body);
            
            if (queueMessage == null || string.IsNullOrEmpty(queueMessage.JobId))
            {
                throw new Exception("Invalid order export message");
            }

            var tableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE") ?? "OrderExportJobs";
            var bucket = Environment.GetEnvironmentVariable("S3_BUCKET") ?? throw new Exception("S3_BUCKET required");

            // Actualizar estado a RUNNING
            var exportJobService = new ExportJobService(_ddbClient, null, tableName, null);
            await exportJobService.UpdateJobStatusToRunningAsync(queueMessage.TenantId, queueMessage.JobId);

            // Obtener credenciales
            var secretManagerService = new SecretManagerService(_secretsManager);
            var spApiSecret = await secretManagerService.GetSpApiSecretAsync(
                queueMessage.TenantId, "prod", "order-export", context);

            if (spApiSecret == null)
            {
                throw new Exception($"Secrets not found for tenant {queueMessage.TenantId}");
            }

            // Crear conexión Amazon SP-API
            var amazonConnection = new AmazonConnection(new AmazonCredential
            {
                ClientId = spApiSecret.ClientId,
                ClientSecret = spApiSecret.ClientSecret,
                RefreshToken = spApiSecret.RefreshToken,
                MarketPlaceID = spApiSecret.MarketPlaceID,
                RoleArn = spApiSecret.RoleArn,
                SellerID = spApiSecret.SellerId
            });

            // Ejecutar exportación
            var orderExportService = new OrderExportService(context);
            var result = await orderExportService.ExportOrdersAsync(
                amazonConnection,
                queueMessage.TenantId,
                queueMessage.JobId,
                DateTime.Parse(queueMessage.StartDate),
                DateTime.Parse(queueMessage.EndDate),
                bucket,
                _s3Client,
                _ddbClient,
                tableName
            );

            context.Logger.LogLine($"Order export completed: Status={result.Status}, Orders={result.TotalOrders}");
        }

        private enum MessageType
        {
            Unknown,
            CatalogOperation,
            OrderExport
        }
    }
}
