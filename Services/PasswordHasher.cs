using System.Security.Cryptography;

namespace HotelHousekeepingApp.Services;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

/// <summary>
/// PBKDF2-SHA256 password hashing. Stored format: "{iterations}.{saltBase64}.{hashBase64}".
/// This exact format/algorithm is what was used to generate the seeded 'admin' password
/// hash in the database script, so it must not be changed without re-seeding.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;      // 128-bit salt
    private const int KeySize = 32;       // 256-bit derived key
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;

        var parts = storedHash.Split('.');
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out int iterations)) return false;

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedKey = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
