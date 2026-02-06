using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace GirlfriendPanel.api.Data;



public sealed record PairRow(
    Guid Id,
    string GfName,
    string BfEmail,
    byte Emoji0,
    byte Emoji1,
    byte Emoji2,
    byte Emoji3,
    DateTime CreatedAt
);

public static class PairRepo
{
    public static SqlConnection Open(string connStr)
        => new SqlConnection(connStr);

    public static async Task<bool> CodeExists(SqlConnection conn, byte e0, byte e1, byte e2, byte e3)
    {
        const string sql = @"
SELECT 1
FROM Pairs
WHERE Emoji0=@e0 AND Emoji1=@e1 AND Emoji2=@e2 AND Emoji3=@e3";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@e0", e0);
        cmd.Parameters.AddWithValue("@e1", e1);
        cmd.Parameters.AddWithValue("@e2", e2);
        cmd.Parameters.AddWithValue("@e3", e3);
        var r = await cmd.ExecuteScalarAsync();
        return r is not null;
    }

    public static async Task Insert(SqlConnection conn, Guid id, string gfName, string bfEmail, byte e0, byte e1, byte e2, byte e3)
    {
        const string sql = @"
INSERT INTO Pairs (Id, GfName, BfEmail, Emoji0, Emoji1, Emoji2, Emoji3, CreatedAt)
VALUES (@id, @gf, @bf, @e0, @e1, @e2, @e3, SYSUTCDATETIME())";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@gf", gfName);
        cmd.Parameters.AddWithValue("@bf", bfEmail);
        cmd.Parameters.AddWithValue("@e0", e0);
        cmd.Parameters.AddWithValue("@e1", e1);
        cmd.Parameters.AddWithValue("@e2", e2);
        cmd.Parameters.AddWithValue("@e3", e3);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<PairRow?> GetById(SqlConnection conn, Guid id)
    {
        const string sql = @"
SELECT Id, GfName, BfEmail, Emoji0, Emoji1, Emoji2, Emoji3, CreatedAt
FROM Pairs
WHERE Id=@id";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);

        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        return new PairRow(
            rd.GetGuid(0),
            rd.GetString(1),
            rd.GetString(2),
            rd.GetByte(3),
            rd.GetByte(4),
            rd.GetByte(5),
            rd.GetByte(6),
            rd.GetDateTime(7)
        );
    }
}