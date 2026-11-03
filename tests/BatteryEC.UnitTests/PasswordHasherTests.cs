using System.Text.RegularExpressions;
using BatteryEC.Infrastructure.Security;
using Xunit;

namespace BatteryEC.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ValidPassword_ReturnsExpectedPbkdf2Format()
    {
        // Arrange
        string password = "TestPassword123!";

        // Act
        string hash = _hasher.HashPassword(password);

        // Assert
        Assert.NotNull(hash);
        // Format: {iterations}.{salt_base64}.{hash_base64}
        var parts = hash.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("100000", parts[0]);

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] subkey = Convert.FromBase64String(parts[2]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, subkey.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void HashPassword_NullOrEmpty_ThrowsArgumentException(string? password)
    {
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(password!));
    }

    [Fact]
    public void VerifyPassword_MatchingPassword_ReturnsTrue()
    {
        // Arrange
        string password = "SuperSecretPassword2026!";
        string hash = _hasher.HashPassword(password);

        // Act
        bool result = _hasher.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        // Arrange
        string password = "SuperSecretPassword2026!";
        string hash = _hasher.HashPassword(password);

        // Act
        bool result = _hasher.VerifyPassword("WrongPassword123!", hash);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("", "100000.abcd.efgh")]
    [InlineData(null, "100000.abcd.efgh")]
    [InlineData("password", "")]
    [InlineData("password", null)]
    public void VerifyPassword_NullOrEmptyInputs_ReturnsFalse(string? password, string? hash)
    {
        bool result = _hasher.VerifyPassword(password!, hash!);
        Assert.False(result);
    }

    [Theory]
    [InlineData("invalid_format")]
    [InlineData("100000.onlytwoparts")]
    [InlineData("invalid_iters.salt.hash")]
    [InlineData("-1.c2FsdA==.aGFzaA==")]
    [InlineData("100000.invalid_base64!.invalid_base64!")]
    public void VerifyPassword_MalformedHash_ReturnsFalse(string malformedHash)
    {
        bool result = _hasher.VerifyPassword("password", malformedHash);
        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_Pbkdf2SeedHash_ReturnsTrueForSeedPassword()
    {
        // Seed hash for P@ssword123 in PostgreSQL
        string seedHash = "100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=";
        string validPassword = "P@ssword123";

        bool result = _hasher.VerifyPassword(validPassword, seedHash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_Pbkdf2SeedHash_ReturnsFalseForWrongPassword()
    {
        string seedHash = "100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=";
        string wrongPassword = "WrongSeedPassword123";

        bool result = _hasher.VerifyPassword(wrongPassword, seedHash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_BcryptFallback_ReturnsTrueForValidBcryptHash()
    {
        string validPassword = "P@ssword123";
        string bcryptHash = BCrypt.Net.BCrypt.HashPassword(validPassword, 11);

        bool result = _hasher.VerifyPassword(validPassword, bcryptHash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_BcryptFallback_ReturnsFalseForWrongPassword()
    {
        string validPassword = "P@ssword123";
        string bcryptHash = BCrypt.Net.BCrypt.HashPassword(validPassword, 11);

        bool result = _hasher.VerifyPassword("WrongPassword123", bcryptHash);

        Assert.False(result);
    }
}
