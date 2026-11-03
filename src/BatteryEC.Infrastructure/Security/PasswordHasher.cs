using System.Security.Cryptography;
using BatteryEC.Application.Common.Interfaces;

namespace BatteryEC.Infrastructure.Security;

/// <summary>
/// Implements PBKDF2 HMAC-SHA256 password hashing with 16-byte salt, 100,000 iterations,
/// constant-time verification, and backward-compatible BCrypt fallback.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;         // 128 bits
    private const int HashSize = 32;         // 256 bits
    private const int DefaultIterations = 100_000;
    private const int MinIterations = 10_000;
    private const int MaxIterations = 500_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;
    private const char Delimiter = '.';

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty or whitespace.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithm,
            HashSize);

        string saltBase64 = Convert.ToBase64String(salt);
        string hashBase64 = Convert.ToBase64String(hash);

        return $"{DefaultIterations}{Delimiter}{saltBase64}{Delimiter}{hashBase64}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        // Backward compatibility: check if hash is legacy BCrypt ($2a$, $2b$, $2y$)
        if (passwordHash.StartsWith("$2a$") || passwordHash.StartsWith("$2b$") || passwordHash.StartsWith("$2y$"))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, passwordHash);
            }
            catch
            {
                return false;
            }
        }

        // Parse PBKDF2 format: {iterations}.{salt_base64}.{hash_base64}
        string[] parts = passwordHash.Split(Delimiter, 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int iterations) || iterations < MinIterations || iterations > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltSize || expectedHash.Length != HashSize)
        {
            return false;
        }

        // Enforce canonical Base64 encoding to prevent encoding malleability and reject tampered padding bits
        if (Convert.ToBase64String(salt) != parts[1] || Convert.ToBase64String(expectedHash) != parts[2])
        {
            return false;
        }

        // Compute candidate hash with the same salt and iteration count
        byte[] candidateHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithm,
            HashSize);

        // Fixed-time comparison avoids timing side-channel attacks
        return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash);
    }
}
