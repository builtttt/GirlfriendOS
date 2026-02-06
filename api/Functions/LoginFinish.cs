using GirlfriendPanel.api.Models;
using GirlfriendPanel.api.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace GirlfriendPanel.api.Functions;

public sealed class LoginFinish
{
    private readonly ILogger _logger;

    public LoginFinish(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LoginFinish>();
    }

    private sealed record ChallengePayload(Guid PairId, long Exp, int[] CorrectIndices, string Nonce);
    private sealed record SessionPayload(Guid PairId, long Exp, string Nonce);

    [Function("LoginFinish")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "login/finish")] HttpRequestData req)
    {
        var hmacSecret = Environment.GetEnvironmentVariable("LOGIN_HMAC_SECRET");
        var sessionSecret = Environment.GetEnvironmentVariable("SESSION_HMAC_SECRET");

        if (string.IsNullOrWhiteSpace(hmacSecret) || string.IsNullOrWhiteSpace(sessionSecret))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Server-Konfiguration fehlt (LOGIN_HMAC_SECRET / SESSION_HMAC_SECRET).");
            return err;
        }

        LoginFinishRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<LoginFinishRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        if (body?.Token is null || body.Answers is null || body.Answers.Length != 4)
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var payload = TokenSigner.VerifyToken<ChallengePayload>(body.Token, hmacSecret);
        if (payload is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.Exp)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (payload.CorrectIndices is null || payload.CorrectIndices.Length != 4)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        for (int i = 0; i < 4; i++)
        {
            var a = body.Answers[i];
            if (a < 0 || a > 8) return req.CreateResponse(HttpStatusCode.BadRequest);
            if (a != payload.CorrectIndices[i])
                return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // issue session token bound to PairId (expires in 12h)
        var sessionPayload = new SessionPayload(
            PairId: payload.PairId,
            Exp: DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
            Nonce: Guid.NewGuid().ToString("N")
        );

        var sessionToken = TokenSigner.CreateToken(sessionPayload, sessionSecret);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new LoginFinishResponse { Ok = true, SessionToken = sessionToken });
        return res;
    }
}
