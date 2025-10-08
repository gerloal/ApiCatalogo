using CatalogWorker;

using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
var factory = LoggerFactory.Create(builder => builder.AddConsole());
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg => cfg.AddJsonFile("appsettings.json", optional: false))
    .ConfigureServices((ctx, services) =>
{
var region = RegionEndpoint.GetBySystemName(ctx.Configuration["AWS:Region"] ?? "eu-west-1");
services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(region));
services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(region));
services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient(region));
services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient(region));
services.AddHostedService<Worker>();
})
    .Build();



await host.RunAsync();

public record UploadMsg(string tenantId, string jobId, string bucket, string key, string fileName);

class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly IAmazonSQS _sqs;
    private readonly IAmazonS3 _s3;
    private readonly IAmazonDynamoDB _ddb;
    private readonly IAmazonSecretsManager _secrets;
    private readonly IConfiguration _cfg;

    public Worker(ILogger<Worker> log, IAmazonSQS sqs, IAmazonS3 s3, IAmazonDynamoDB ddb, IAmazonSecretsManager secrets, IConfiguration cfg)
        => (_log, _sqs, _s3, _ddb, _secrets, _cfg) = (log, sqs, s3, ddb, secrets, cfg);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = _cfg["QueueUrl"]!;
        var table = _cfg["TableName"]!;
        while (!stoppingToken.IsCancellationRequested)
        {
            var resp = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = 5,
                WaitTimeSeconds = 10
            }, stoppingToken);

            foreach (var m in resp.Messages)
            {
                try
                {
                    var msg = JsonSerializer.Deserialize<UploadMsg>(m.Body)!;
                    using var _ = _log.BeginScope(new Dictionary<string, object?> { ["tenantId"] = msg.tenantId, ["jobId"] = msg.jobId });

                    // 1) Leer secretos del tenant (simulación)
                    var refresh = await _secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = $"/spapi/{msg.tenantId}/lwa-refresh" }, stoppingToken);
                    _log.LogInformation("Usando secreto LWA length={len}", refresh.SecretString?.Length);

                    // 2) Descargar objeto de S3 (simulación de parseo)
                    var s3obj = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = msg.bucket, Key = msg.key }, stoppingToken);
                    _log.LogInformation("Descargado {Key} de {Bucket}, bytes={Len}", msg.key, msg.bucket, s3obj.Headers.ContentLength);

                    // 3) Actualizar estado en DynamoDB: RUNNING -> SUCCEEDED
                    var pk = $"TENANT#{msg.tenantId}";
                    // Si tu SK exacto no lo tienes, puedes guardar SK en el mensaje; aquí asumimos formato yyyyMMdd en el propio jobId si lo usas así
                    var sk = $"JOB#{DateTime.UtcNow:yyyyMMdd}#{msg.jobId}";
                    await _ddb.UpdateItemAsync(new UpdateItemRequest
                    {
                        TableName = table,
                        Key = new() { ["pk"] = new AttributeValue(pk), ["sk"] = new AttributeValue(sk) },
                        UpdateExpression = "SET #s = :done, finishedAt = :t",
                        ExpressionAttributeNames = new() { ["#s"] = "status" },
                        ExpressionAttributeValues = new()
                        {
                            [":done"] = new AttributeValue("SUCCEEDED"),
                            [":t"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                        }
                    }, stoppingToken);

                    // 4) Borrar mensaje
                    await _sqs.DeleteMessageAsync(queueUrl, m.ReceiptHandle, stoppingToken);
                    _log.LogInformation("Procesado OK y borrado de SQS");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error procesando mensaje");
                    // No borramos el mensaje -> SQS lo reentregará y acabará en la DLQ si falla repetidamente
                }
            }
        }
    }
}

