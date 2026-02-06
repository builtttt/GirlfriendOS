using GirlfriendPanel.api.Data;
using GirlfriendPanel.api.Models;
using GirlfriendPanel.api.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;

namespace GirlfriendPanel.api.Functions;


public sealed class LoginStart
{
    private readonly ILogger _logger;
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public LoginStart(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LoginStart>();
    }
    private sealed record ChallengePayload(Guid PairId, long Exp, int[] CorrectIndices, string Nonce);

    [Function("LoginStart")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "login/start")] HttpRequestData req)
    {
        var hmacSecret = Environment.GetEnvironmentVariable("LOGIN_HMAC_SECRET");
        var connStr = Environment.GetEnvironmentVariable("SqlConnectionString");

        if (string.IsNullOrWhiteSpace(hmacSecret) || string.IsNullOrWhiteSpace(connStr))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Server-Konfiguration fehlt (LOGIN_HMAC_SECRET / SqlConnectionString).");
            return err;
        }

        var pairIdStr = System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("pairId");
        if (!Guid.TryParse(pairIdStr, out var pairId))
            return await Bad(req, "pairId fehlt/ungültig.");

        using var conn = PairRepo.Open(connStr);
        await conn.OpenAsync();
        var pair = await PairRepo.GetById(conn, pairId);
        if (pair is null)
            return req.CreateResponse(HttpStatusCode.NotFound);

        var seq = new[] { (int)pair.Emoji0, (int)pair.Emoji1, (int)pair.Emoji2, (int)pair.Emoji3 };

        var boards = new int[4][];
        var correctIdx = new int[4];

        for (int step = 0; step < 4; step++)
        {
            boards[step] = BuildBoard(seq[step]);
            correctIdx[step] = Array.IndexOf(boards[step], seq[step]);
        }

        var payload = new ChallengePayload(
            PairId: pairId,
            Exp: DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeSeconds(),
            CorrectIndices: correctIdx,
            Nonce: Guid.NewGuid().ToString("N")
        );

        var token = TokenSigner.CreateToken(payload, hmacSecret);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new LoginStartResponse { Boards = boards, Token = token });
        return res;
    }

    private static int NextInt(int maxExclusive)
    {
        var bytes = new byte[4];
        while (true)
        {
            Rng.GetBytes(bytes);
            var v = BitConverter.ToUInt32(bytes, 0);
            var limit = (uint.MaxValue / (uint)maxExclusive) * (uint)maxExclusive;
            if (v < limit) return (int)(v % (uint)maxExclusive);
        }
    }

    private static int[] BuildBoard(int correctEmojiId)
    {
        var set = new HashSet<int> { correctEmojiId };
        while (set.Count < 9) set.Add(NextInt(64));

        var board = set.ToArray();
        for (int i = board.Length - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (board[i], board[j]) = (board[j], board[i]);
        }
        return board;
    }

    private static async Task<HttpResponseData> Bad(HttpRequestData req, string msg)
    {
        var r = req.CreateResponse(HttpStatusCode.BadRequest);
        await r.WriteStringAsync(msg);
        return r;
    }
}
