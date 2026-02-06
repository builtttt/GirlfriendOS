using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GirlfriendPanel.api.Models;

public sealed class RegisterRequest
{
    public string? GfName { get; set; }
    public string? BfEmail { get; set; }
}

public sealed class RegisterResponse
{
    public required Guid PairId { get; init;}
    public required int[] EmojiIds { get; init; } // length 4
}
