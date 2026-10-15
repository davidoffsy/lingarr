using System.Security.Cryptography;
using System.Text;

namespace Lingarr.Server.Services;

public static class SecretComparer
{
    public static bool Equals(string? provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided ?? string.Empty);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        if (providedBytes.Length != expectedBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
