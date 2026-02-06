using GirlfriendPanel.api.Models;
using GirlfriendPanel.api.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Net;
using System.Text.Json;

namespace GirlfriendPanel.api.Functions;

public sealed class SendStatus
{
    private readonly ILogger _logger;

    public SendStatus(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SendStatus>();
    }

    [Function("SendStatus")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "status")] HttpRequestData req)
    {
        var sessionSecret = Environment.GetEnvironmentVariable("SESSION_HMAC_SECRET");
        if (string.IsNullOrWhiteSpace(sessionSecret))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Server-Konfiguration fehlt (SESSION_HMAC_SECRET).");
            return err;
        }

        if (!TryGetBearer(req, out var bearer))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var session = TokenSigner.VerifyToken<SessionPayload>(bearer, sessionSecret);
        if (session is null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > session.exp)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // Read JSON
        GfStatusRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<GfStatusRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid JSON");
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Ungültiges JSON.");
            return bad;
        }

        if (payload is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Body fehlt.");
            return bad;
        }

        // Basic validation
        static bool InRange(int v) => v is >= 0 and <= 100;
        if (!InRange(payload.Mood) || !InRange(payload.Hunger) || !InRange(payload.Energy) || !InRange(payload.Stress))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Werte müssen zwischen 0 und 100 sein.");
            return bad;
        }

        // Env
        var apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
        var fromEmail = Environment.GetEnvironmentVariable("MAIL_FROM");
        var fromName = Environment.GetEnvironmentVariable("MAIL_FROM_NAME") ?? "GF Status Panel";
        var toEmail = Environment.GetEnvironmentVariable("MAIL_TO");

        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(fromEmail) ||
            string.IsNullOrWhiteSpace(toEmail))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Server-Konfiguration fehlt (SENDGRID_API_KEY / MAIL_FROM / MAIL_TO).");
            return err;
        }

        // Format email
        var needsText = (payload.Needs is { Length: > 0 })
            ? string.Join(", ", payload.Needs)
            : "keine";

        var boyfriendAction = ComputeAction(payload);

        var subject = $"GF Status: {boyfriendAction}";
        var textBody =
            $@"Neuer Status 💌

            Stimmung: {payload.Mood}/100
            Hunger:   {payload.Hunger}/100
            Energie:  {payload.Energy}/100
            Stress:   {payload.Stress}/100
            Needs:    {needsText}

            Aktion: {boyfriendAction}
            ";
        var htmlBody = $@"
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset=""UTF-8"" />
              <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
            </head>
            <body style=""margin:0;padding:0;background:linear-gradient(135deg,#fdf2f8,#e0f2fe);font-family:Segoe UI,Arial,sans-serif;"">
              <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""padding:20px 10px;"">
                <tr><td align=""center"">
                  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:520px;background:#ffffff;border-radius:18px;box-shadow:0 10px 30px rgba(0,0,0,0.08);padding:24px;"">
                    <tr><td align=""center"">
                      <div style=""font-size:26px;"">💌</div>
                      <h2 style=""margin:6px 0 0 0;color:#be185d;"">Neuer GF Status</h2>
                      <p style=""margin:6px 0 0 0;color:#6b7280;font-size:13px;"">Sie hat dir ein Update geschickt</p>
                    </td></tr>

                    <tr><td style=""padding-top:20px;"">
                      <table width=""100%"" cellpadding=""8"" cellspacing=""0"" style=""font-size:14px;"">
                        <tr><td>😄 Stimmung</td><td align=""right""><strong>{payload.Mood} / 100</strong></td></tr>
                        <tr><td>🍔 Hunger</td><td align=""right""><strong>{payload.Hunger} / 100</strong></td></tr>
                        <tr><td>⚡ Energie</td><td align=""right""><strong>{payload.Energy} / 100</strong></td></tr>
                        <tr><td>🔥 Stress</td><td align=""right""><strong>{payload.Stress} / 100</strong></td></tr>
                        <tr><td>💖 Bedürfnisse</td><td align=""right""><strong>{WebUtility.HtmlEncode(needsText)}</strong></td></tr>
                      </table>
                    </td></tr>

                    <tr><td align=""center"" style=""padding:22px 0 10px 0;"">
                      <div style=""background:linear-gradient(135deg,#f9a8d4,#a5b4fc);color:white;border-radius:14px;padding:14px 16px;font-size:15px;font-weight:600;"">
                        👉 Empfohlene Aktion: {WebUtility.HtmlEncode(boyfriendAction)}
                      </div>
                    </td></tr>
                  </table>

                  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:520px;margin-top:16px;"">
                    <tr><td align=""center"" style=""color:#6b7280;font-size:12px;"">
                      Du erhältst diese Nachricht, weil dein GF Status Panel aktiv ist 💖
                    </td></tr>
                  </table>

                </td></tr>
              </table>
            </body>
            </html>";

        // Send
        try
        {
            var client = new SendGridClient(apiKey);
            var msg = MailHelper.CreateSingleEmail(
                new EmailAddress(fromEmail, fromName),
                new EmailAddress(toEmail),
                subject,
                textBody,
                htmlBody
            );

            var resp = await client.SendEmailAsync(msg);

            if ((int)resp.StatusCode >= 400)
            {
                _logger.LogWarning("SendGrid failed: {Status}", resp.StatusCode);
                var fail = req.CreateResponse(HttpStatusCode.BadGateway);
                await fail.WriteStringAsync($"E-Mail Versand fehlgeschlagen: {resp.StatusCode}");
                return fail;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send failed");
            var fail = req.CreateResponse(HttpStatusCode.BadGateway);
            await fail.WriteStringAsync("E-Mail Versand fehlgeschlagen.");
            return fail;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteStringAsync("OK");
        return ok;
    }

    private static string ComputeAction(GfStatusRequest p)
    {
        var needs = new HashSet<string>((p.Needs ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);

        if (p.Hunger >= 85) return "SOFORT Essen organisieren";
        if (p.Stress >= 80) return "Beruhigen + Umarmen";
        if (needs.Contains("kiss") && needs.Contains("cuddle")) return "Kuss + Kuscheln (Notfall)";
        if (needs.Contains("kiss")) return "Kuss geben";
        if (needs.Contains("cuddle")) return "Kuscheln";
        if (needs.Contains("message")) return "Schreib ihr";
        if (needs.Contains("call")) return "Anrufen";
        if (p.Mood <= 20) return "Sehr lieb sein";
        return "Bereitschaft halten";
    }
    
    private sealed record SessionPayload(long exp, string nonce);

    private static bool TryGetBearer(HttpRequestData req, out string token)
    {
        token = "";
        if (!req.Headers.TryGetValues("Authorization", out var values)) return false;
        var h = values.FirstOrDefault() ?? "";
        if (!h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        token = h["Bearer ".Length..].Trim();
        return token.Length > 20;
    }
}

