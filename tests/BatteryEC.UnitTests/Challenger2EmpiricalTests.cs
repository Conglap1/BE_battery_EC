using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BatteryEC.Application.Common.Models;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BatteryEC.UnitTests;

/// <summary>
/// Empirical Challenger 2 Verification:
/// 1. JWT Token Structure, Header (HS256), Expiration (15m), Claims (sub/NameIdentifier, name/unique_name, email, role, phone, jti), Multi-role claims, Signature validation & tampering.
/// 2. Refresh Token Entropy: 1,000 samples, 0 collisions, 64-byte raw entropy (88 Base64 characters).
/// 3. Live EF Core queries and mutations against PostgreSQL (User, Role, RefreshToken, RefreshTokenExpiryTime read/write/clear).
/// </summary>
public class Challenger2EmpiricalTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=battery_ec;Username=postgres;Password=postgres";

    private readonly JwtSettings _jwtSettings = new()
    {
        SecretKey = "SuperSecretSecurityKeyBatteryECProject2026Min32BytesLength!",
        Issuer = "BatteryECWebApi",
        Audience = "BatteryECApp",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    private JwtTokenGenerator CreateGenerator(JwtSettings? settings = null)
    {
        return new JwtTokenGenerator(Options.Create(settings ?? _jwtSettings));
    }

    private BatteryDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();
        return new BatteryDbContext(optionsBuilder.Options);
    }

    #region 1. JWT Token Structure & Header (HS256)

    [Fact]
    public void JwtToken_Header_AlgorithmIsHS256_AndTypeIsJwt()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User
        {
            UserId = 1,
            Username = "admin",
            FullName = "Admin User",
            Email = "admin@example.com"
        };

        // Act
        string tokenString = generator.GenerateAccessToken(user, new[] { "Admin" });

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        // Header validations
        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal("JWT", jwt.Header.Typ);
    }

    #endregion

    #region 2. JWT Lifetime (Exactly 15 Minutes)

    [Fact]
    public void JwtToken_Lifetime_ValidFromToValidTo_IsExactly15Minutes()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User
        {
            UserId = 10,
            Username = "tester",
            FullName = "Tester"
        };

        // Act
        DateTime before = DateTime.UtcNow;
        string tokenString = generator.GenerateAccessToken(user, new[] { "User" });
        DateTime after = DateTime.UtcNow;

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        // Check ValidTo - ValidFrom (tolerance < 1 second for Unix second truncation)
        var lifetime = jwt.ValidTo - jwt.ValidFrom;
        Assert.True(Math.Abs(lifetime.TotalMinutes - 15.0) < 0.05,
            $"Expected lifetime to be 15 minutes, but got {lifetime.TotalMinutes} minutes.");

        // Check that ValidTo is in the future within [before + 14m50s, after + 15m10s]
        Assert.True(jwt.ValidTo >= before.AddMinutes(14).AddSeconds(50));
        Assert.True(jwt.ValidTo <= after.AddMinutes(15).AddSeconds(10));
    }

    #endregion

    #region 3. Claims Verification (sub, name, email, role, phone, jti)

    [Fact]
    public void JwtToken_Claims_ContainsAllRequiredClaims()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User
        {
            UserId = 999,
            Username = "johndoe",
            FullName = "Johnathan Doe",
            Email = "john.doe@example.com",
            Phone = "0987654321"
        };
        var roles = new[] { "WarehouseStaff" };

        // Act
        string tokenString = generator.GenerateAccessToken(user, roles);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        // 1. sub / NameIdentifier
        var subClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == JwtRegisteredClaimNames.Sub ||
            c.Type == "nameid" ||
            c.Type == ClaimTypes.NameIdentifier);
        Assert.NotNull(subClaim);
        Assert.Equal("999", subClaim.Value);

        // 2. name / unique_name
        var nameClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == "unique_name" ||
            c.Type == ClaimTypes.Name);
        Assert.NotNull(nameClaim);
        Assert.Equal("johndoe", nameClaim.Value);

        // 3. email
        var emailClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == JwtRegisteredClaimNames.Email ||
            c.Type == "email" ||
            c.Type == ClaimTypes.Email);
        Assert.NotNull(emailClaim);
        Assert.Equal("john.doe@example.com", emailClaim.Value);

        // 4. phone
        var phoneClaim = jwt.Claims.FirstOrDefault(c => c.Type == "phone");
        Assert.NotNull(phoneClaim);
        Assert.Equal("0987654321", phoneClaim.Value);

        // 5. jti (must be a valid Guid)
        var jtiClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == JwtRegisteredClaimNames.Jti ||
            c.Type == "jti");
        Assert.NotNull(jtiClaim);
        Assert.True(Guid.TryParse(jtiClaim.Value, out _), "jti claim is not a valid GUID.");

        // 6. role
        var roleClaims = jwt.Claims
            .Where(c => c.Type == "role" || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();
        Assert.Contains("WarehouseStaff", roleClaims);
    }

    [Fact]
    public void JwtToken_Claims_WhenUsernameIsEmpty_FallsBackToFullName()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User
        {
            UserId = 100,
            Username = "",
            FullName = "Fallback FullName",
            Email = "fallback@example.com"
        };

        // Act
        string tokenString = generator.GenerateAccessToken(user, new[] { "User" });

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        var nameClaim = jwt.Claims.FirstOrDefault(c =>
            c.Type == "unique_name" ||
            c.Type == ClaimTypes.Name);
        Assert.NotNull(nameClaim);
        Assert.Equal("Fallback FullName", nameClaim.Value);
    }

    [Fact]
    public void JwtToken_Claims_WhenPhoneIsNull_DoesNotEmitPhoneClaim()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User
        {
            UserId = 101,
            Username = "nophone",
            FullName = "No Phone User",
            Phone = null
        };

        // Act
        string tokenString = generator.GenerateAccessToken(user, new[] { "User" });

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        var phoneClaim = jwt.Claims.FirstOrDefault(c => c.Type == "phone" || c.Type == ClaimTypes.MobilePhone);
        Assert.Null(phoneClaim);
    }

    #endregion

    #region 4. Multi-Role Claims Verification

    [Fact]
    public void JwtToken_MultiRoleClaims_EmitsAllAssignedRoles()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User
        {
            UserId = 50,
            Username = "superstaff",
            FullName = "Multi Role Staff"
        };

        // Test with all 6 RBAC roles simultaneously
        var allRoles = new[]
        {
            "Admin",
            "StoreManager",
            "WarehouseStaff",
            "SalesStaff",
            "Technician",
            "User"
        };

        // Act
        string tokenString = generator.GenerateAccessToken(user, allRoles);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        var emittedRoles = jwt.Claims
            .Where(c => c.Type == "role" || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet();

        Assert.Equal(6, emittedRoles.Count);
        foreach (var role in allRoles)
        {
            Assert.Contains(role, emittedRoles);
        }
    }

    [Fact]
    public void JwtToken_EmptyOrWhitespaceRoles_AreFilteredOut()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User { UserId = 51, Username = "testuser", FullName = "Test" };
        var roles = new[] { "Admin", "", "   ", "Technician" };

        // Act
        string tokenString = generator.GenerateAccessToken(user, roles);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        var emittedRoles = jwt.Claims
            .Where(c => c.Type == "role" || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        Assert.Equal(2, emittedRoles.Count);
        Assert.Contains("Admin", emittedRoles);
        Assert.Contains("Technician", emittedRoles);
    }

    #endregion

    #region 5. Cryptographic Validation & Tampering Resistance

    [Fact]
    public void JwtToken_CryptographicValidation_ValidKeySucceeds_TamperedPayloadOrSignatureFails()
    {
        // Arrange
        var generator = CreateGenerator();
        var user = new User { UserId = 200, Username = "cryptouser", FullName = "Crypto User" };
        string tokenString = generator.GenerateAccessToken(user, new[] { "User" });

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Act & Assert 1: Valid token succeeds
        var principal = handler.ValidateToken(tokenString, validationParameters, out var validatedToken);
        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);

        // Act & Assert 2: Tampered payload (valid JSON, but signature mismatch) fails signature validation
        string[] parts = tokenString.Split('.');
        Assert.Equal(3, parts.Length);

        // Decode payload JSON, modify claim, re-encode to valid Base64Url
        byte[] payloadBytes = Base64UrlEncoder.DecodeBytes(parts[1]);
        var jsonNode = JsonNode.Parse(Encoding.UTF8.GetString(payloadBytes))!;
        jsonNode["role"] = "Admin"; // Privilege escalation attempt
        string tamperedPayloadEncoded = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(jsonNode.ToJsonString()));

        string tamperedPayloadToken = $"{parts[0]}.{tamperedPayloadEncoded}.{parts[2]}";
        Assert.ThrowsAny<SecurityTokenInvalidSignatureException>(() =>
            handler.ValidateToken(tamperedPayloadToken, validationParameters, out _));

        // Act & Assert 3: Tampered signature fails
        string tamperedSignature = parts[2][..^4] + "XXXX";
        string tamperedSignatureToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";
        Assert.ThrowsAny<SecurityTokenInvalidSignatureException>(() =>
            handler.ValidateToken(tamperedSignatureToken, validationParameters, out _));

        // Act & Assert 4: Wrong key fails validation
        var wrongKeyParameters = validationParameters.Clone();
        wrongKeyParameters.IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("WrongSecretKeyThatDoesNotMatchOriginalSecretKeyMin32B!"));

        Assert.ThrowsAny<SecurityTokenInvalidSignatureException>(() =>
            handler.ValidateToken(tokenString, wrongKeyParameters, out _));
    }

    #endregion

    #region 6. Refresh Token Entropy & Zero Collisions

    [Fact]
    public void RefreshToken_1000Samples_HaveZeroCollisions_64BytesLength_And88Base64Chars()
    {
        // Arrange
        var generator = CreateGenerator();
        const int sampleCount = 1000; // Well exceeds the required 100
        var tokenSet = new HashSet<string>(sampleCount);
        var byteCounts = new int[256];

        // Act
        for (int i = 0; i < sampleCount; i++)
        {
            string token = generator.GenerateRefreshToken();

            // Assert format
            Assert.NotNull(token);
            Assert.Equal(88, token.Length); // 64 raw bytes in Base64 -> 88 chars (ends in ==)
            Assert.EndsWith("==", token);

            // Decode bytes
            byte[] rawBytes = Convert.FromBase64String(token);
            Assert.Equal(64, rawBytes.Length);

            // Count byte occurrences for entropy check
            foreach (byte b in rawBytes)
            {
                byteCounts[b]++;
            }

            // Assert uniqueness (no collisions)
            bool isAdded = tokenSet.Add(token);
            Assert.True(isAdded, $"Collision detected at iteration {i} for token: {token}");
        }

        // Assert 0 collisions across 1,000 samples
        Assert.Equal(sampleCount, tokenSet.Count);

        // Entropy distribution check: With 64,000 random bytes, each byte 0..255 expected ~250 times.
        // Every byte value 0..255 must appear at least 100 times (statistically guaranteed for CSPRNG).
        for (int b = 0; b < 256; b++)
        {
            Assert.True(byteCounts[b] > 100,
                $"Byte value {b} appeared only {byteCounts[b]} times in 64,000 random bytes, indicating non-randomness.");
        }
    }

    #endregion

    #region 7. Live EF Core Queries & Mutations Against PostgreSQL

    [Fact]
    public async Task LivePostgreSql_QueryUsersAndRoles_SucceedsWithoutErrors()
    {
        // Arrange
        await using var context = CreateDbContext();

        // Act
        var roles = await context.Roles.OrderBy(r => r.RoleId).ToListAsync();
        var users = await context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .ToListAsync();

        // Assert
        Assert.True(roles.Count >= 6, $"Expected at least 6 roles in database, found {roles.Count}.");
        var roleNames = roles.Select(r => r.Name).ToHashSet();
        Assert.Contains("Admin", roleNames);
        Assert.Contains("StoreManager", roleNames);
        Assert.Contains("WarehouseStaff", roleNames);
        Assert.Contains("SalesStaff", roleNames);
        Assert.Contains("Technician", roleNames);
        Assert.Contains("User", roleNames);

        Assert.NotEmpty(users);
        var adminUser = users.FirstOrDefault(u => u.Username == "admin");
        Assert.NotNull(adminUser);
        Assert.Equal("admin@batterypartsstore.vn", adminUser.Email);
        Assert.Contains(adminUser.UserRoles, ur => ur.Role.Name == "Admin");
    }

    [Fact]
    public async Task LivePostgreSql_RefreshToken_ReadWriteAndClear_Succeeds()
    {
        // Arrange
        var generator = CreateGenerator();
        string testRefreshToken = generator.GenerateRefreshToken();
        DateTime testExpiryUtc = DateTime.UtcNow.AddDays(7);

        await using (var writeContext = CreateDbContext())
        {
            var user = await writeContext.Users.FirstOrDefaultAsync(u => u.Username == "admin");
            Assert.NotNull(user);

            // Record previous values to restore later
            string? previousToken = user.RefreshToken;
            DateTime? previousExpiry = user.RefreshTokenExpiryTime;

            try
            {
                // Act 1: Write RefreshToken and RefreshTokenExpiryTime
                user.RefreshToken = testRefreshToken;
                user.RefreshTokenExpiryTime = testExpiryUtc;
                int affected = await writeContext.SaveChangesAsync();
                Assert.True(affected > 0);

                // Act 2: Read back in an isolated DbContext instance (bypass change tracker cache)
                await using (var readContext = CreateDbContext())
                {
                    var reloadedUser = await readContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == "admin");
                    Assert.NotNull(reloadedUser);
                    Assert.Equal(testRefreshToken, reloadedUser.RefreshToken);
                    Assert.NotNull(reloadedUser.RefreshTokenExpiryTime);

                    // Allow microsecond timestamp difference due to PostgreSQL timestamptz precision
                    var diff = Math.Abs((reloadedUser.RefreshTokenExpiryTime.Value - testExpiryUtc).TotalSeconds);
                    Assert.True(diff < 1.0, $"Timestamp diff too large: {diff}s");
                    Assert.Equal(DateTimeKind.Utc, reloadedUser.RefreshTokenExpiryTime.Value.Kind);
                }

                // Act 3: Clear RefreshToken (logout/revocation simulation)
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
                await writeContext.SaveChangesAsync();

                // Act 4: Verify cleared in fresh context
                await using (var readContext2 = CreateDbContext())
                {
                    var clearedUser = await readContext2.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == "admin");
                    Assert.NotNull(clearedUser);
                    Assert.Null(clearedUser.RefreshToken);
                    Assert.Null(clearedUser.RefreshTokenExpiryTime);
                }
            }
            finally
            {
                // Cleanup: restore previous values
                user.RefreshToken = previousToken;
                user.RefreshTokenExpiryTime = previousExpiry;
                await writeContext.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task LivePostgreSql_RefreshTokenExpiryTime_RequiresUtc_UnspecifiedThrowsException()
    {
        // Demonstrates and verifies Npgsql requirement: timestamptz requires DateTimeKind.Utc
        await using var context = CreateDbContext();
        var user = await context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
        Assert.NotNull(user);

        // Attempting to save DateTime with Kind = Unspecified to timestamptz
        DateTime unspecifiedTime = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Unspecified);
        user.RefreshTokenExpiryTime = unspecifiedTime;

        // In Npgsql 6.0+, this is an InvalidCastException or DbUpdateException wrapping it
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await context.SaveChangesAsync();
        });
    }

    #endregion
}
