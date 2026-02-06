using GirlfriendPanel.api.Models;
using GirlfriendPanel.api.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GirlfriendPanel.api.Functions;


public sealed class LoginStart
{
    private readonly ILogger _logger;
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public LoginStart(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LoginStart>();
    }

    private sealed record ChallengePayload(long Exp, int[] CorrectIndices, string Nonce);

    [Function("LoginStart")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "login/start")] HttpRequestData req)
    {
        var hmacSecret = Environment.GetEnvironmentVariable("LOGIN_HMAC_SECRET");
        var pairSecret = Environment.GetEnvironmentVariable("PAIR_SECRET_EMOJI_IDS"); 
        // example: "12,5,33,7" (4 ints from 0..63)

        if (string.IsNullOrWhiteSpace(hmacSecret) || string.IsNullOrWhiteSpace(pairSecret))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Server-Konfiguration fehlt (LOGIN_HMAC_SECRET / PAIR_SECRET_EMOJI_IDS).");
            return err;
        }

        int[] seq;
        try
        {
            seq = pairSecret.Split(',').Select(s => int.Parse(s.Trim())).ToArray();
            if (seq.Length != 4 || seq.Any(x => x < 0 || x > 63)) throw new Exception();
        }
        catch
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("PAIR_SECRET_EMOJI_IDS ist ungültig (muss 4 Zahlen 0..63 sein).");
            return err;
        }

        var boards = new int[4][];
        var correctIdx = new int[4];

        for (int step = 0; step < 4; step++)
        {
            boards[step] = BuildBoard(seq[step]);              // 9 emojiIds
            correctIdx[step] = Array.IndexOf(boards[step], seq[step]); // 0..8
        }

        var payload = new ChallengePayload(
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
}