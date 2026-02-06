using GirlfriendPanel.api.Data;
using GirlfriendPanel.api.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GirlfriendPanel.api.Functions;

public sealed class Register
{
    private readonly ILogger _logger;
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public Register(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<Register>();
    }

    [Function("Register")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "register")] HttpRequestData req)
    {
        var connStr = Environment.GetEnvironmentVariable("SqlConnectionString");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Server-Konfiguration fehlt (SqlConnectionString).");
            return err;
        }

        RegisterRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RegisterRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        var gfName = (body?.GfName ?? "").Trim();
        var bfEmail = (body?.BfEmail ?? "").Trim().ToLowerInvariant();

        if (gfName.Length < 1 || gfName.Length > 50)
            return await Bad(req, "GfName ungültig.");

        if (!bfEmail.Contains("@") || bfEmail.Length > 200)
            return await Bad(req, "BfEmail ungültig.");

        // generate unique emoji code
        byte e0, e1, e2, e3;
        using var conn = PairRepo.Open(connStr);
        await conn.OpenAsync();

        while (true)
        {
            (e0, e1, e2, e3) = (NextEmojiId(), NextEmojiId(), NextEmojiId(), NextEmojiId());

            // optional: avoid duplicates inside code (nicer)
            if (e0 == e1 || e0 == e2 || e0 == e3 || e1 == e2 || e1 == e3 || e2 == e3)
                continue;

            var exists = await PairRepo.CodeExists(conn, e0, e1, e2, e3);
            if (!exists) break;
        }

        var id = Guid.NewGuid();
        await PairRepo.Insert(conn, id, gfName, bfEmail, e0, e1, e2, e3);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new RegisterResponse
        {
            PairId = id,
            EmojiIds = new[] { (int)e0, (int)e1, (int)e2, (int)e3 }
        });
        return res;
    }

    private static byte NextEmojiId()
    {
        // 0..63
        var b = new byte[1];
        while (true)
        {
            Rng.GetBytes(b);
            if (b[0] < 64) return b[0];
        }
    }

    private static async Task<HttpResponseData> Bad(HttpRequestData req, string msg)
    {
        var r = req.CreateResponse(HttpStatusCode.BadRequest);
        await r.WriteStringAsync(msg);
        return r;
    }
}