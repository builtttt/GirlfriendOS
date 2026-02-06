using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GirlfriendPanel.api.Models {
    public sealed class LoginStartResponse
    {
        public required int[][] Boards { get; init; }   // 4 x 9 emojiIds
        public required string Token { get; init; }     // signed challenge token
    }
    public sealed class LoginFinishResponse
    {
        public bool Ok { get; init; }
        public string? SessionToken { get; init; } // send as Bearer from frontend
    }
    public sealed class LoginFinishRequest
    {
        public string? Token { get; set; }
        public int[]? Answers { get; set; } // length 4, each 0..8
    }
}
