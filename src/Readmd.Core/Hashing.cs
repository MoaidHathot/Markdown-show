using System.Security.Cryptography;
using System.Text;

namespace Readmd.Core;

public static class Hashing
{
    public static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    public static string Sha256Hex(ReadOnlySpan<byte> input)
    {
        var bytes = SHA256.HashData(input);
        return Convert.ToHexStringLower(bytes);
    }
}
