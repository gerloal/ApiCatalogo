using Amazon.CloudWatch;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.XRay.Recorder.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using FikaAmazonAPI;
using FikaAmazonAPI.Parameter;
using FuncionLambda.Models;
using FuncionLambda.Services;
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    public class OrderExportWorker
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonDynamoDB _ddbClient;
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly string _tableName;
        private readonly string _bucket;
        private readonly CloudWatchMetricsService _metrics;

        public OrderExportWorker()
        {
            AWSSDKHandler.RegisterXRayForAllServices();
            _s3Client = new AmazonS3Client();
            _ddbClient = new AmazonDynamoDBClient();
            _secretsManager = new AmazonSecretsManagerClient();
            _tableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE") ?? "OrderExportJobs";
            _bucket = Environment.GetEnvironmentVariable("S3_BUCKET") ?? throw new Exception("S3_BUCKET environment variable is required");
            _metrics = new CloudWatchMetricsService(new AmazonCloudWatchClient());
        }

        /// <summary>
        /// Constructor para testing (cloudWatch opcional para no romper tests existentes)
        /// </summary>
        public OrderExportWorker(IAmazonS3 s3Client, IAmazonDynamoDB ddbClient, IAmazonSecretsManager secretsManager, string tableName, string bucket, IAmazonCloudWatch cloudWatch = null)
        {
            _s3Client = s3Client;
            _ddbClient = ddbClient;
            _secretsManager = secretsManager;
            _tableName = tableName;
            _bucket = bucket;
            _metrics = cloudWatch != null ? new CloudWatchMetricsService(cloudWatch) : null;
        }

        /// <summary>
        /// Handler para mensajes SQS
        /// </summary>
        public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing {sqsEvent.Records.Count} messages");

            foreach (var record in sqsEvent.Records)
            {
                try
                {
                    await ProcessMessageAsync(record, context);
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Error processing message {record.MessageId}: {ex.Message}");
                    context.Logger.LogLine($"StackTrace: {ex.StackTrace}");

                    if (_metrics != null)
                    {
                        var tenantId = TryExtractTenantId(record.Body);
                        try { await _metrics.PublishExportJobFailedAsync(tenantId); } catch { /* no bloquear el rethrow */ }
                    }

                    throw;
                }
            }
        }

        private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
        {
            context.Logger.LogLine($"Processing message: {message.Body}");

            // Parsear mensaje
            ExportOrdersQueueMessage queueMessage;
            try
            {
                queueMessage = JsonSerializer.Deserialize<ExportOrdersQueueMessage>(message.Body);
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error parsing message body: {ex.Message}");
                throw;
            }

            if (queueMessage == null || string.IsNullOrEmpty(queueMessage.JobId) || string.IsNullOrEmpty(queueMessage.TenantId))
            {
                context.Logger.LogLine("Invalid message: missing JobId or TenantId");
                throw new Exception("Invalid message format");
            }

            context.Logger.LogLine($"Processing export job {queueMessage.JobId} for tenant {queueMessage.TenantId}");

            var jobStartTime = DateTime.UtcNow;

            // Actualizar estado a RUNNING
            var exportJobService = new ExportJobService(_ddbClient, null, _tableName, null);
            await exportJobService.UpdateJobStatusToRunningAsync(queueMessage.TenantId, queueMessage.JobId);

            // Obtener credenciales de Amazon SP-API desde Secrets Manager
            var secretManagerService = new SecretManagerService(_secretsManager);
            var spApiSecret = await secretManagerService.GetSpApiSecretAsync(queueMessage.TenantId, "dev", "order-export", context);

            if (spApiSecret == null)
            {
                throw new Exception($"Secrets not found for tenant {queueMessage.TenantId}");
            }

            // Crear conexi�n Amazon SP-API
            var amazonConnection = new AmazonConnection(new AmazonCredential
            {
                ClientId = spApiSecret.ClientId,
                ClientSecret = spApiSecret.ClientSecret,
                RefreshToken = spApiSecret.RefreshToken,
                MarketPlaceID = spApiSecret.MarketPlaceID,
                RoleArn = spApiSecret.RoleArn,
                SellerID = spApiSecret.SellerId
            });

            // Parsear fechas
            var dateParseStyles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;
            DateTime startDate = DateTime.Parse(queueMessage.StartDate, CultureInfo.InvariantCulture, dateParseStyles);
            DateTime endDate = DateTime.Parse(queueMessage.EndDate, CultureInfo.InvariantCulture, dateParseStyles);

            var latestAllowedEndDate = DateTime.UtcNow.AddMinutes(-3);
            if (endDate > latestAllowedEndDate)
            {
                context.Logger.LogLine($"EndDate {endDate:o} adjusted to {latestAllowedEndDate:o} to satisfy SP-API CreatedBefore requirement");
                endDate = latestAllowedEndDate;
            }

            // Ejecutar exportaci�n
            var orderExportService = new OrderExportService(context);
            var result = await orderExportService.ExportOrdersAsync(
                amazonConnection,
                queueMessage.TenantId,
                queueMessage.JobId,
                startDate,
                endDate,
                _bucket,
                _s3Client,
                _ddbClient,
                _tableName
            );

            context.Logger.LogLine($"Export completed for job {queueMessage.JobId}: Status={result.Status}, Orders={result.TotalOrders}, Lines={result.TotalLines}");

            if (_metrics != null)
            {
                var duration = (DateTime.UtcNow - jobStartTime).TotalSeconds;
                try { await _metrics.PublishOrdersExportedAsync(queueMessage.TenantId, "Amazon", result.TotalOrders, result.TotalLines, duration); } catch { /* no bloquear el flujo principal */ }
            }
        }

        private static string TryExtractTenantId(string messageBody)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<ExportOrdersQueueMessage>(messageBody);
                return msg?.TenantId ?? "unknown";
            }
            catch { return "unknown"; }
        }
    }
}
