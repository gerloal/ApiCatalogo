using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using FikaAmazonAPI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Sellers;


namespace FuncionLambda
{
    public class Program
    {
        public static void Main() { } // punto de entrada vacío
    }

    public class Function
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly IAmazonS3 _s3;


        private static string SanitizeBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            if (body[0] == '\uFEFF') body = body[1..];                       // BOM
            const string MisdecodedBom = "\u00EF\u00BB\u00BF";
            if (body.StartsWith(MisdecodedBom, StringComparison.Ordinal)) body = body[MisdecodedBom.Length..];
            return body.Trim();
        }

        public Function() : this(new AmazonDynamoDBClient(), new AmazonS3Client()) { }
        public Function(IAmazonDynamoDB ddb, IAmazonS3 s3) { _ddb = ddb; _s3 = s3; }

        // ✅ Handler de SQS
        public async Task<SQSBatchResponse> HandlerAsync(SQSEvent evnt, ILambdaContext ctx)
        {
            var jobsTable = Environment.GetEnvironmentVariable("JOBS_TABLE") ?? "catalog-api-dev-jobs";
            var env = Environment.GetEnvironmentVariable("ENV") ?? "dev";
            var project = Environment.GetEnvironmentVariable("PROJECT_NAME") ?? "catalog-api";
             
            var batchFailures = new List<SQSBatchResponse.BatchItemFailure>();

            foreach (var rec in evnt.Records)
            {
                try
                {
                    var body = SanitizeBody(rec.Body);

                    // Usa tu modelo QueueMsg (jobId, tenantId, bucket, key, etc.)
                    var msg = JsonSerializer.Deserialize<QueueMsg>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? throw new Exception("Invalid message body");

                    await CatalogoService.MarkAsNewJobReceived(_ddb, jobsTable, msg.tenantId, msg.jobId, msg.bucket, msg.key, msg.operation); 


                    await CatalogoService.MarkJobProcessingAsync(_ddb, jobsTable, msg.jobId, msg.tenantId, "PROCESSING");

                                        
                    await CatalogoService.ProcessClientOperation(msg.operation, msg.tenantId, _s3, msg.bucket, msg.key, env, project, ctx);            


                    await CatalogoService.MarkJobProcessingAsync(_ddb, jobsTable, msg.jobId, msg.tenantId, "DONE");
                }
                catch
                {

                    batchFailures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = rec.MessageId });
                }
            }

            // Partial batch response: solo se reintentan los que fallaron
            return new SQSBatchResponse(batchFailures);
        }
    }
}