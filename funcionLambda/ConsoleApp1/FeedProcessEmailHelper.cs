using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Amazon.SQS.Model;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Notifications;
using System.Text.Json;
using System.Text.Json.Serialization;
using Destination = Amazon.SimpleEmailV2.Model.Destination;

namespace FuncionLambda
{


    public class FeedProcessEmailHelper
    {
        private readonly IAmazonSimpleEmailServiceV2 _ses;
        private readonly string _from;              // Debe ser tu identidad verificada en SES si no tienes dominio
        private readonly string _cfgSetName;        // opcional (Configuration Set)
        private readonly string _region;

        public FeedProcessEmailHelper(
            string fromEmailIdentity,
            string awsRegion = "eu-west-1",
            string configurationSetName = null)
        {
            _from = fromEmailIdentity;
            _region = awsRegion;
            _cfgSetName = configurationSetName;
            _ses = new AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(awsRegion));
        }

        /// <summary>
        /// Recibe el JSON del feed processing result y envía un correo con resumen + incidencias.
        /// </summary>
        /// <param name="toEmail">Destinatario (cliente)</param>
        /// <param name="tenantId">Tenant</param>
        /// <param name="feedResultJson">JSON de la respuesta de SP-API (issues + summary + header)</param>
        /// <param name="fallbackSubjectSuffix">Texto adicional para el asunto si faltan campos</param>
        /// <param name="replyTo">Opcional: dirección Reply-To</param>
        /// <param name="reportLink">Opcional: enlace a reporte en S3 (pre-firmado) u otro</param>
        public async Task SendAsync(
            string toEmail,
            string tenantId,
            string feedResultJson,
            string fallbackSubjectSuffix = null,
            string replyTo = null,
            string reportLink = null,
            string blindCopy = null)
        {
            var model = ParseFeedResult(feedResultJson);

            var feedId = model?.Header?.FeedId ?? "N/A";
            var sellerId = model?.Header?.SellerId ?? "N/A";
            var status = GuessStatus(model); // si lo tienes explícito, pásalo y evita el guess
            var processed = model?.Summary?.MessagesProcessed ?? 0;
            var accepted = model?.Summary?.MessagesAccepted ?? 0;
            var invalid = model?.Summary?.MessagesInvalid ?? 0;

            var subject = $"[{tenantId}] Resultado FEED {feedId} – {status}";
            if (!string.IsNullOrWhiteSpace(fallbackSubjectSuffix))
                subject += $" – {fallbackSubjectSuffix}";

            var issuesText = BuildIssuesText(model);

            var summaryText =
    $@"Tenant: {tenantId}
SellerId: {sellerId}
FeedId: {feedId}
Estado: {status}
Procesados: {processed}
Aceptados: {accepted}
Inválidos: {invalid}
";

            var textBody = $"{summaryText}\nIncidencias:\n{issuesText}";
            if (!string.IsNullOrEmpty(reportLink))
                textBody += $"\n\nReporte: {reportLink}";

            var htmlBody =
    $@"<html><body>
  <h3>Resultado del FEED</h3>
  <table border='0' cellpadding='6'>
    <tr><td><b>Tenant</b></td><td>{Html(tenantId)}</td></tr>
    <tr><td><b>SellerId</b></td><td>{Html(sellerId)}</td></tr>
    <tr><td><b>FeedId</b></td><td>{Html(feedId)}</td></tr>
    <tr><td><b>Estado</b></td><td>{Html(status)}</td></tr>
    <tr><td><b>Procesados</b></td><td>{processed}</td></tr>
    <tr><td><b>Aceptados</b></td><td>{accepted}</td></tr>
    <tr><td><b>Inválidos</b></td><td>{invalid}</td></tr>
  </table>
  <h4>Incidencias</h4>
  <pre style='white-space:pre-wrap;font-family:monospace'>{Html(issuesText)}</pre>
  {(string.IsNullOrEmpty(reportLink) ? "" : $"<p><b>Reporte:</b> <a href=\"{Html(reportLink)}\">abrir</a></p>")}
</body></html>";

            var request = new SendEmailRequest
            {
                FromEmailAddress = _from,
                Destination = new Destination
                {
                    ToAddresses = new List<string> { toEmail },
                    BccAddresses = !string.IsNullOrWhiteSpace(blindCopy) ? new List<string> { blindCopy }: new List<string>()
                },
                Content = new EmailContent
                {
                    Simple = new Amazon.SimpleEmailV2.Model.Message
                    {
                        Subject = new Content { Data = subject },
                        Body = new Body
                        {
                            Text = new Content { Data = textBody },
                            Html = new Content { Data = htmlBody }
                        }
                    }
                },
                ConfigurationSetName = _cfgSetName
            };

            if (!string.IsNullOrWhiteSpace(replyTo))
                request.ReplyToAddresses = new List<string> { replyTo };

            var resp = await _ses.SendEmailAsync(request);
            // TIP: guarda resp.MessageId para trazabilidad
        }

        // ===== Helpers =====

        private static string Html(string s) =>
            System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        private static string BuildIssuesText(FeedResult model)
        {
            if (model?.Issues == null || model.Issues.Count == 0)
                return "Sin incidencias.";

            var lines = new List<string>(model.Issues.Count);
            foreach (var i in model.Issues)
            {
                var mid = i.MessageId?.ToString() ?? "?";
                var sev = i.Severity ?? "?";
                var code = i.Code ?? "?";
                var msg = i.Message ?? "";
                lines.Add($"- #{mid} [{sev}] {code}: {msg}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string GuessStatus(FeedResult model)
        {
            // Si tu lógica ya obtiene el estado real (DONE, CANCELLED, FATAL),
            // reemplaza este "guess" por el valor real.
            if (model?.Summary == null) return "UNKNOWN";
            if (model.Summary.MessagesInvalid > 0 && model.Summary.MessagesAccepted == 0)
                return "DONE_WITH_ERRORS";
            if (model.Summary.MessagesAccepted > 0 && model.Summary.MessagesInvalid == 0)
                return "DONE";
            if (model.Summary.MessagesAccepted > 0 && model.Summary.MessagesInvalid > 0)
                return "DONE_PARTIAL";
            return "DONE";
        }

        private static FeedResult ParseFeedResult(string json)
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            return JsonSerializer.Deserialize<FeedResult>(json, opts);
        }

        // ===== DTOs mínimos para mapear tu JSON de SP-API =====
        public class FeedResult
        {
            public Header Header { get; set; }
            public List<Issue> Issues { get; set; }
            public Summary Summary { get; set; }
        }

        public class Header
        {
            [JsonPropertyName("sellerId")] public string SellerId { get; set; }
            [JsonPropertyName("version")] public string Version { get; set; }
            [JsonPropertyName("feedId")] public string FeedId { get; set; }
        }

        public class Issue
        {
            [JsonPropertyName("messageId")] public int? MessageId { get; set; }
            [JsonPropertyName("code")] public string Code { get; set; }
            [JsonPropertyName("severity")] public string Severity { get; set; }
            [JsonPropertyName("message")] public string Message { get; set; }
        }

        public class Summary
        {
            [JsonPropertyName("errors")] public int Errors { get; set; }
            [JsonPropertyName("warnings")] public int Warnings { get; set; }
            [JsonPropertyName("messagesProcessed")] public int MessagesProcessed { get; set; }
            [JsonPropertyName("messagesAccepted")] public int MessagesAccepted { get; set; }
            [JsonPropertyName("messagesInvalid")] public int MessagesInvalid { get; set; }
        }
    }

    /// <summary>
    /// Error a nivel de artículo individual en un feed de marketplaces.
    /// </summary>
    public class MarketplaceItemError
    {
        public string Sku       { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;  // "Precio", "Stock", etc.
        public string Reason    { get; set; } = string.Empty;
    }

    /// <summary>
    /// Datos de resultado para mercados no-Amazon (Mirakl, Miravia, AliExpress…).
    /// </summary>
    public class MarketplaceFeedResult
    {
        public string Marketplace   { get; set; }
        public string TenantId      { get; set; }
        public string Status        { get; set; }   // DONE / DONE_WITH_ERRORS / FAILED
        public string Summary       { get; set; }   // Texto libre con el resumen (.ToSummary())
        public List<string> Errors  { get; set; } = new();
        /// <summary>Errores por artículo concreto (SKU + operación + motivo).</summary>
        public List<MarketplaceItemError> ItemErrors { get; set; } = new();
    }

    /// <summary>
    /// Helper estático para enviar emails de resultado en mercados no-Amazon.
    /// </summary>
    public static class MarketplaceEmailHelper
    {
        private const string BLIND_COPY = "german.lopezalmuzara@gmail.com";

    /// <summary>
    /// Envía un email de resultado genérico para mercados no-Amazon (PcComponentes, Miravia, AliExpress, Decathlon).
    /// No envía nada si <paramref name="toEmail"/> está vacío.
    /// </summary>
    public static async Task SendGenericResultAsync(
        string fromEmail,
        string toEmail,
        MarketplaceFeedResult result,
        string awsRegion = "eu-west-1")
    {
        if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(fromEmail))
            return;

        var status = result.Status ?? (result.Errors.Count == 0 && result.ItemErrors.Count == 0 ? "DONE" : "DONE_WITH_ERRORS");
        var subject = $"[{Html(result.TenantId)}] {Html(result.Marketplace)} FeedCatalog – {Html(status)}";

        // ─── Cuerpo texto plano ───────────────────────────────────────
        var errorsText = result.ItemErrors.Count > 0
            ? string.Join(Environment.NewLine, result.ItemErrors.Select(e => $"- [{e.Operation}] SKU {e.Sku}: {e.Reason}"))
            : result.Errors.Count == 0
                ? "Sin errores."
                : string.Join(Environment.NewLine, result.Errors.Select(e => $"- {e}"));

        var textBody =
$@"Marketplace : {result.Marketplace}
Tenant      : {result.TenantId}
Estado      : {status}
Resumen     : {result.Summary}

Errores:
{errorsText}";

        // ─── Sección HTML de errores ──────────────────────────────────
        string errorsHtml;
        if (result.ItemErrors.Count > 0)
        {
            var rows = new System.Text.StringBuilder();
            foreach (var e in result.ItemErrors)
                rows.Append($"<tr><td style='padding:4px 8px;border:1px solid #ccc'>{Html(e.Sku)}</td>" +
                            $"<td style='padding:4px 8px;border:1px solid #ccc'>{Html(e.Operation)}</td>" +
                            $"<td style='padding:4px 8px;border:1px solid #ccc'>{Html(e.Reason)}</td></tr>");
            errorsHtml =
                $"<table border='0' cellpadding='0' cellspacing='0' style='border-collapse:collapse;font-size:13px'>" +
                $"<thead><tr>" +
                $"<th style='padding:4px 8px;border:1px solid #999;background:#f0f0f0'>SKU</th>" +
                $"<th style='padding:4px 8px;border:1px solid #999;background:#f0f0f0'>Operación</th>" +
                $"<th style='padding:4px 8px;border:1px solid #999;background:#f0f0f0'>Motivo</th>" +
                $"</tr></thead><tbody>{rows}</tbody></table>";
        }
        else if (result.Errors.Count > 0)
        {
            var li = string.Join("", result.Errors.Select(e => $"<li>{Html(e)}</li>"));
            errorsHtml = $"<ul style='font-family:monospace;font-size:13px'>{li}</ul>";
        }
        else
        {
            errorsHtml = "<p style='color:green'>Sin errores.</p>";
        }

        var htmlBody =
$@"<html><body>
  <h3>Resultado FeedCatalog — {Html(result.Marketplace)}</h3>
  <table border='0' cellpadding='6'>
    <tr><td><b>Marketplace</b></td><td>{Html(result.Marketplace)}</td></tr>
    <tr><td><b>Tenant</b></td><td>{Html(result.TenantId)}</td></tr>
    <tr><td><b>Estado</b></td><td>{Html(status)}</td></tr>
    <tr><td><b>Resumen</b></td><td>{Html(result.Summary)}</td></tr>
  </table>
  <h4>Artículos con error</h4>
  {errorsHtml}
</body></html>";

        var ses = new AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(awsRegion));
        var request = new SendEmailRequest
        {
            FromEmailAddress = fromEmail,
            Destination      = new Destination
            {
                ToAddresses  = new List<string> { toEmail },
                BccAddresses = new List<string> { BLIND_COPY }
            },
            Content          = new EmailContent
            {
                Simple = new Amazon.SimpleEmailV2.Model.Message
                {
                    Subject = new Content { Data = subject },
                    Body    = new Body
                    {
                        Text = new Content { Data = textBody },
                        Html = new Content { Data = htmlBody }
                    }
                }
            }
        };
        await ses.SendEmailAsync(request);
    }

    private static string Html(string s) =>
        System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

    } // end MarketplaceEmailHelper

}
