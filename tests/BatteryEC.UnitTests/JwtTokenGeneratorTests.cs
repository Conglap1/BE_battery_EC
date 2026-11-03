using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BatteryEC.Application.Common.Models;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEC.UnitTests;

public class JwtTokenGeneratorTests
{
    private readonly JwtSettings _validSettings = new()
    {
        SecretKey = "SuperSecretSecurityKeyBatteryECProject2026Min32BytesLength!",
        Issuer = "BatteryECWebApi",
        Audience = "BatteryECApp",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    [Fact]
    public void GenerateAccessToken_ValidUserAndRoles_ProducesValidTokenWithAllClaims()
    {
        // Arrange
        var options = Options.Create(_validSettings);
        var generator = new JwtTokenGenerator(options);

        var user = new User
        {
            UserId = 42,
            Username = "testuser",
            Email = "testuser@example.com",
            Phone = "0901234567",
            FullName = "Test User"
        };
        var roles = new[] { "Admin", "StoreManager" };

        // Act
        string tokenString = generator.GenerateAccessToken(user, roles);

        // Assert
        Assert.NotNull(tokenString);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        Assert.Equal(_validSettings.Issuer, jwt.Issuer);
        Assert.Contains(_validSettings.Audience, jwt.Audiences);

        // Verify Claims
        var subClaim = jwt.Claims.FirstOrDefault(c => c.Type == "nameid" || c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier);
        Assert.NotNull(subClaim);
        Assert.Equal("42", subClaim.Value);

        var nameClaim = jwt.Claims.FirstOrDefault(c => c.Type == "unique_name" || c.Type == ClaimTypes.Name);
        Assert.NotNull(nameClaim);
        Assert.Equal("testuser", nameClaim.Value);

        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == ClaimTypes.Email);
        Assert.NotNull(emailClaim);
        Assert.Equal("testuser@example.com", emailClaim.Value);

        var roleClaims = jwt.Claims.Where(c => c.Type == "role" || c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains("Admin", roleClaims);
        Assert.Contains("StoreManager", roleClaims);

        var phoneClaim = jwt.Claims.FirstOrDefault(c => c.Type == "phone");
        Assert.NotNull(phoneClaim);
        Assert.Equal("0901234567", phoneClaim.Value);

        // Verify Expiration is within 15 minutes
        var expectedExpiry = DateTime.UtcNow.AddMinutes(15);
        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddMinutes(14) && jwt.ValidTo <= DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void GenerateAccessToken_ShortSecretKey_ThrowsInvalidOperationException()
    {
        var shortKeySettings = new JwtSettings
        {
            SecretKey = "ShortKeyUnder32Bytes",
            Issuer = "BatteryECWebApi",
            Audience = "BatteryECApp"
        };
        var generator = new JwtTokenGenerator(Options.Create(shortKeySettings));

        var user = new User { UserId = 1, Username = "admin" };

        Assert.Throws<InvalidOperationException>(() => generator.GenerateAccessToken(user, new[] { "Admin" }));
    }

    [Fact]
    public void GenerateRefreshToken_Returns64ByteBase64String()
    {
        // Arrange
        var generator = new JwtTokenGenerator(Options.Create(_validSettings));

        // Act
        string refreshToken = generator.GenerateRefreshToken();

        // Assert
        Assert.NotNull(refreshToken);
        Assert.Equal(88, refreshToken.Length); // 64 bytes Base64-encoded is 88 characters

        byte[] decoded = Convert.FromBase64String(refreshToken);
        Assert.Equal(64, decoded.Length);
    }
}
