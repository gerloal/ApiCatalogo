using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using FuncionLambda.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda.Services
{
    public class ExportJobService
    {
        private readonly IAmazonDynamoDB _ddbClient;
        private readonly IAmazonSQS _sqsClient;
        private readonly string _tableName;
        private readonly string _queueUrl;

        public ExportJobService(
            IAmazonDynamoDB ddbClient,
            IAmazonSQS sqsClient,
            string tableName,
            string queueUrl)
        {
            _ddbClient = ddbClient;
            _sqsClient = sqsClient;
            _tableName = tableName;
            _queueUrl = queueUrl;
        }

        /// <summary>
        /// Crea un nuevo job de exportación
        /// </summary>
        public async Task<string> CreateExportJobAsync(ExportOrdersRequest request)
        {
            var jobId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("o");

            var pk = $"TENANT#{request.TenantId}";
            var sk = $"JOB#{jobId}";

            var item = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new AttributeValue { S = pk },
                ["sk"] = new AttributeValue { S = sk },
                ["tenantId"] = new AttributeValue { S = request.TenantId },
                ["jobId"] = new AttributeValue { S = jobId },
                ["status"] = new AttributeValue { S = "PENDING" },
                ["startDate"] = new AttributeValue { S = request.StartDate.ToString("o") },
                ["endDate"] = new AttributeValue { S = request.EndDate.ToString("o") },
                ["format"] = new AttributeValue { S = request.Format },
                ["createdAt"] = new AttributeValue { S = now },
                ["updatedAt"] = new AttributeValue { S = now },
                ["totalOrders"] = new AttributeValue { N = "0" },
                ["totalLines"] = new AttributeValue { N = "0" }
            };

            var putRequest = new PutItemRequest
            {
                TableName = _tableName,
                Item = item
            };

            await _ddbClient.PutItemAsync(putRequest);

            // Enviar mensaje a SQS
            var queueMessage = new ExportOrdersQueueMessage
            {
                TenantId = request.TenantId,
                JobId = jobId,
                StartDate = request.StartDate.ToString("o"),
                EndDate = request.EndDate.ToString("o"),
                Format = request.Format,
                Operation = "EXPORT_ORDERS"
            };

            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = JsonSerializer.Serialize(queueMessage)
            };

            await _sqsClient.SendMessageAsync(sendMessageRequest);

            return jobId;
        }

        /// <summary>
        /// Obtiene el estado de un job
        /// </summary>
        public async Task<JobStatusResponse> GetJobStatusAsync(string tenantId, string jobId)
        {
            var pk = $"TENANT#{tenantId}";
            var sk = $"JOB#{jobId}";

            var getRequest = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["pk"] = new AttributeValue { S = pk },
                    ["sk"] = new AttributeValue { S = sk }
                }
            };

            var response = await _ddbClient.GetItemAsync(getRequest);

            if (!response.IsItemSet || response.Item.Count == 0)
            {
                return null;
            }

            var item = response.Item;

            var jobStatus = new JobStatusResponse
            {
                JobId = GetStringValue(item, "jobId"),
                TenantId = GetStringValue(item, "tenantId"),
                Status = GetStringValue(item, "status"),
                TotalOrders = GetIntValue(item, "totalOrders"),
                TotalLines = GetIntValue(item, "totalLines"),
                HeadersPresignedUrl = GetStringValue(item, "headersUrl"),
                LinesPresignedUrl = GetStringValue(item, "linesUrl"),
                ErrorMessage = GetStringValue(item, "errorMessage"),
                CreatedAt = GetStringValue(item, "createdAt"),
                UpdatedAt = GetStringValue(item, "updatedAt")
            };

            return jobStatus;
        }

        /// <summary>
        /// Actualiza el estado de un job a RUNNING
        /// </summary>
        public async Task UpdateJobStatusToRunningAsync(string tenantId, string jobId)
        {
            var pk = $"TENANT#{tenantId}";
            var sk = $"JOB#{jobId}";

            var updateRequest = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["pk"] = new AttributeValue { S = pk },
                    ["sk"] = new AttributeValue { S = sk }
                },
                UpdateExpression = "SET #s = :status, updatedAt = :now",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#s"] = "status"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":status"] = new AttributeValue { S = "RUNNING" },
                    [":now"] = new AttributeValue { S = DateTime.UtcNow.ToString("o") }
                }
            };

            await _ddbClient.UpdateItemAsync(updateRequest);
        }

        private string GetStringValue(Dictionary<string, AttributeValue> item, string key)
        {
            return item.ContainsKey(key) && item[key].S != null ? item[key].S : null;
        }

        private int GetIntValue(Dictionary<string, AttributeValue> item, string key)
        {
            if (item.ContainsKey(key) && item[key].N != null && int.TryParse(item[key].N, out var value))
            {
                return value;
            }
            return 0;
        }
    }
}
