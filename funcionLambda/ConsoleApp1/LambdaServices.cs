using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.SQS;
using FuncionLambda.Models;
using FuncionLambda.Services;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    public class LambdaServices
    {
        private readonly IAmazonDynamoDB _ddbClient;
        private readonly IAmazonSQS _sqsClient;
        private readonly string _tableName;
        private readonly string _queueUrl;

        public LambdaServices()
        {
            _ddbClient = new AmazonDynamoDBClient();
            _sqsClient = new AmazonSQSClient();
            _tableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE") ?? "OrderExportJobs";
            _queueUrl = Environment.GetEnvironmentVariable("SQS_QUEUE_URL") ?? throw new Exception("SQS_QUEUE_URL environment variable is required");
        }

        /// <summary>
        /// Constructor para testing
        /// </summary>
        public LambdaServices(IAmazonDynamoDB ddbClient, IAmazonSQS sqsClient, string tableName, string queueUrl)
        {
            _ddbClient = ddbClient;
            _sqsClient = sqsClient;
            _tableName = tableName;
            _queueUrl = queueUrl;
        }

        /// <summary>
        /// Handler principal para API Gateway
        /// </summary>
        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                context.Logger.LogLine($"Received request: {request.HttpMethod} {request.Path}");

                var service = new ExportJobService(_ddbClient, _sqsClient, _tableName, _queueUrl);

                // Routing basado en método y path
                if (request.HttpMethod == "POST" && request.Path == "/exports/orders")
                {
                    return await HandleCreateExportAsync(request, context, service);
                }
                else if (request.HttpMethod == "GET" && request.Path.StartsWith("/exports/orders/"))
                {
                    return await HandleGetJobStatusAsync(request, context, service);
                }
                else
                {
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 404,
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                        Body = JsonSerializer.Serialize(new { error = "Not Found" })
                    };
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error: {ex.Message}");
                context.Logger.LogLine($"StackTrace: {ex.StackTrace}");

                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Internal Server Error", message = ex.Message })
                };
            }
        }

        /// <summary>
        /// Maneja POST /exports/orders
        /// </summary>
        private async Task<APIGatewayProxyResponse> HandleCreateExportAsync(
            APIGatewayProxyRequest request,
            ILambdaContext context,
            ExportJobService service)
        {
            // Validar autenticación (puedes implementar validación de token aquí)
            if (!ValidateAuth(request, context))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 401,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Unauthorized" })
                };
            }

            // Parsear body
            ExportOrdersRequest exportRequest;
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                exportRequest = JsonSerializer.Deserialize<ExportOrdersRequest>(request.Body, options);
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error parsing request body: {ex.Message}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Bad Request", message = "Invalid JSON body" })
                };
            }

            // Validar input
            var validationError = ValidateExportRequest(exportRequest);
            if (validationError != null)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Bad Request", message = validationError })
                };
            }

            // Crear job
            var jobId = await service.CreateExportJobAsync(exportRequest);

            context.Logger.LogLine($"Export job created: {jobId} for tenant: {exportRequest.TenantId}");

            var response = new ExportOrdersResponse
            {
                JobId = jobId,
                Status = "PENDING",
                Message = "Export job created successfully. Use GET /exports/orders/{jobId} to check status."
            };

            return new APIGatewayProxyResponse
            {
                StatusCode = 202,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                Body = JsonSerializer.Serialize(response)
            };
        }

        /// <summary>
        /// Maneja GET /exports/orders/{jobId}
        /// </summary>
        private async Task<APIGatewayProxyResponse> HandleGetJobStatusAsync(
            APIGatewayProxyRequest request,
            ILambdaContext context,
            ExportJobService service)
        {
            // Validar autenticación
            if (!ValidateAuth(request, context))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 401,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Unauthorized" })
                };
            }

            // Extraer jobId del path
            var pathParts = request.Path.Split('/');
            var jobId = pathParts.Length > 3 ? pathParts[3] : null;

            if (string.IsNullOrEmpty(jobId))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Bad Request", message = "JobId is required" })
                };
            }

            // Obtener tenantId de query params o headers
            string tenantId = null;
            if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey("tenantId"))
            {
                tenantId = request.QueryStringParameters["tenantId"];
            }
            else if (request.Headers != null && request.Headers.ContainsKey("X-Tenant-Id"))
            {
                tenantId = request.Headers["X-Tenant-Id"];
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Bad Request", message = "TenantId is required (query param or X-Tenant-Id header)" })
                };
            }

            var jobStatus = await service.GetJobStatusAsync(tenantId, jobId);

            if (jobStatus == null)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 404,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                    Body = JsonSerializer.Serialize(new { error = "Not Found", message = $"Job {jobId} not found for tenant {tenantId}" })
                };
            }

            context.Logger.LogLine($"Job status retrieved: {jobId} - Status: {jobStatus.Status}");

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                Body = JsonSerializer.Serialize(jobStatus)
            };
        }

        /// <summary>
        /// Valida autenticación básica (puedes mejorar esto con JWT, API Key, etc.)
        /// </summary>
        private bool ValidateAuth(APIGatewayProxyRequest request, ILambdaContext context)
        {
            // Ejemplo básico: validar API Key en headers
            if (request.Headers != null && request.Headers.ContainsKey("X-Api-Key"))
            {
                var apiKey = request.Headers["X-Api-Key"];
                var validApiKey = Environment.GetEnvironmentVariable("API_KEY");
                
                if (!string.IsNullOrEmpty(validApiKey) && apiKey == validApiKey)
                {
                    return true;
                }
            }

            // Si no hay API_KEY configurada en variables de entorno, aceptar por defecto (desarrollo)
            var envApiKey = Environment.GetEnvironmentVariable("API_KEY");
            if (string.IsNullOrEmpty(envApiKey))
            {
                context.Logger.LogLine("Warning: No API_KEY configured, accepting all requests");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Valida la petición de exportación
        /// </summary>
        private string ValidateExportRequest(ExportOrdersRequest request)
        {
            if (request == null)
            {
                return "Request body is required";
            }

            if (string.IsNullOrEmpty(request.TenantId))
            {
                return "TenantId is required";
            }

            if (request.StartDate == default(DateTime))
            {
                return "StartDate is required";
            }

            if (request.EndDate == default(DateTime))
            {
                return "EndDate is required";
            }

            if (request.StartDate > request.EndDate)
            {
                return "StartDate must be before EndDate";
            }

            // Validar que el rango no sea mayor a 30 días
            if ((request.EndDate - request.StartDate).TotalDays > 30)
            {
                return "Date range cannot exceed 30 days";
            }

            if (!string.IsNullOrEmpty(request.Format) && request.Format.ToUpper() != "CSV")
            {
                return "Only CSV format is supported";
            }

            return null;
        }
    }
}
