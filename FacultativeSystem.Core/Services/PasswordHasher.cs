using System.Security.Cryptography;
using System.Text;

namespace FacultativeSystem.Core.Services;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            return string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string password, string expectedHash)
    {
        var actual = Hash(password);
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
