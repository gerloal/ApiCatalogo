using Amazon;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SQS;
using Amazon.SQS.Model;
using FuncionLambda.Models;
using FuncionLambda.Services;
using Microsoft.Extensions.Configuration;

using System.Text.Json;


namespace FuncionLambda.Code
{
    class Program
    {
        static string SanitizeBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;

            // 1) BOM real
            if (body.Length > 0 && body[0] == '\uFEFF')
                body = body.Substring(1);

            // 2) Secuencia mal decodificada "ï»¿" al inicio
            const string MisdecodedBom = "\u00EF\u00BB\u00BF";
            if (body.StartsWith(MisdecodedBom, StringComparison.Ordinal))
                body = body.Substring(MisdecodedBom.Length);

            // 3) Espacios/saltos de línea residuales
            return body.Trim();
        }

        static async Task Main(string[] args)
        {
          

            
            var region = RegionEndpoint.EUWest1; // eu-west-1
            var queueUrl = "https://sqs.eu-west-1.amazonaws.com/340663646958/catalog-jobs";
            var jobsTable = "catalog-api-dev-jobs";
            var project = "catalog-api";
            var env = "env";

            using var sqs = new AmazonSQSClient(region);
            using var s3 = new AmazonS3Client(region);
            using var sm = new AmazonSecretsManagerClient(region);
            using var ddb = new AmazonDynamoDBClient(region);

            Console.WriteLine("Worker started. Long-polling SQS…");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };


            while (!cts.IsCancellationRequested)
            {
                var resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20,           // long polling
                    VisibilityTimeout = 120         // ajusta a tu tiempo medio de proceso
                }, cts.Token);

                if (resp.Messages.Count == 0) continue;

                foreach (var m in resp.Messages)
                {
                    try
                    {
                        var body = SanitizeBody(m.Body);

                        var msg = JsonSerializer.Deserialize<QueueMsg>(body, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = false
                        }) ?? throw new Exception("Invalid message body");

                        await CatalogoService.MarkAsNewJobReceived(ddb, jobsTable, msg.jobId, msg.tenantId);

                        // 1) Idempotencia: cambiar estado del job a PROCESSING si estaba PENDING
                        await CatalogoService.MarkJobProcessingAsync(ddb, jobsTable, msg.jobId, msg.tenantId, "PROCESSING");

                        // 2) Descargar fichero de S3
                        var tmpPath = Path.Combine(Path.GetTempPath(), $"{msg.jobId}.dat");
                        //await CatalogoService.DownloadAsync(s3, msg.bucket, msg.key, tmpPath);

                        // 6) Marcar DONE
                        await CatalogoService.MarkJobProcessingAsync(ddb, jobsTable, msg.jobId, msg.tenantId, "DONE");

                        // 7) Borrar mensaje
                        await sqs.DeleteMessageAsync(queueUrl, m.ReceiptHandle);

                        // Limpieza
                        //File.Delete(tmpPath);
                    }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }
                }
            }

            // See https://aka.ms/new-console-template for more information
            Console.WriteLine("Hello, World!");
        }
    }
}