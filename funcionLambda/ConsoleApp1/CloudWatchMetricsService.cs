using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuncionLambda
{
    /// <summary>
    /// Publica métricas personalizadas en CloudWatch bajo el namespace MultiMarketplace/FuncionLambda.
    /// </summary>
    public class CloudWatchMetricsService
    {
        private const string Namespace = "MultiMarketplace/FuncionLambda";

        private readonly IAmazonCloudWatch _cloudWatch;

        public CloudWatchMetricsService(IAmazonCloudWatch cloudWatch)
        {
            _cloudWatch = cloudWatch;
        }

        /// <summary>
        /// Métricas al completar una exportación de pedidos (órdenes, líneas, duración).
        /// </summary>
        public async Task PublishOrdersExportedAsync(
            string tenantId,
            string marketplace,
            int totalOrders,
            int totalLines,
            double durationSeconds)
        {
            var now = DateTime.UtcNow;
            var dimsTenantMarket = new List<Dimension>
            {
                new() { Name = "TenantId",    Value = tenantId    },
                new() { Name = "Marketplace", Value = marketplace }
            };
            var dimsTenant = new List<Dimension>
            {
                new() { Name = "TenantId", Value = tenantId }
            };

            await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
            {
                Namespace  = Namespace,
                MetricData = new List<MetricDatum>
                {
                    new() { MetricName = "OrdersExported",     Value = totalOrders,     Unit = StandardUnit.Count,   Timestamp = now, Dimensions = dimsTenantMarket },
                    new() { MetricName = "OrderLinesExported", Value = totalLines,      Unit = StandardUnit.Count,   Timestamp = now, Dimensions = dimsTenantMarket },
                    new() { MetricName = "ExportDuration",     Value = durationSeconds, Unit = StandardUnit.Seconds, Timestamp = now, Dimensions = dimsTenant       }
                }
            });
        }

        /// <summary>
        /// Métrica al crear un nuevo job de exportación desde la API.
        /// </summary>
        public async Task PublishExportJobCreatedAsync(string tenantId)
        {
            await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
            {
                Namespace  = Namespace,
                MetricData = new List<MetricDatum>
                {
                    new()
                    {
                        MetricName = "ExportJobCreated",
                        Value      = 1,
                        Unit       = StandardUnit.Count,
                        Timestamp  = DateTime.UtcNow,
                        Dimensions = new List<Dimension> { new() { Name = "TenantId", Value = tenantId } }
                    }
                }
            });
        }

        /// <summary>
        /// Métrica cuando un job de exportación falla.
        /// </summary>
        public async Task PublishExportJobFailedAsync(string tenantId)
        {
            await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
            {
                Namespace  = Namespace,
                MetricData = new List<MetricDatum>
                {
                    new()
                    {
                        MetricName = "ExportJobFailed",
                        Value      = 1,
                        Unit       = StandardUnit.Count,
                        Timestamp  = DateTime.UtcNow,
                        Dimensions = new List<Dimension> { new() { Name = "TenantId", Value = tenantId } }
                    }
                }
            });
        }
    }
}
