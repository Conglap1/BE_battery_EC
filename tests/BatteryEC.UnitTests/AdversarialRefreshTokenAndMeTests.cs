using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Domain.Enums;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Security;
using BatteryEC.Infrastructure.Services;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BatteryEC.UnitTests;

/// <summary>
/// Adversarial & empirical stress tests for Dual-Channel Refresh Token and GET /api/auth/me endpoint.
/// Tests live AuthController, AuthService, CurrentUserService against PostgreSQL.
/// </summary>
public class AdversarialRefreshTokenAndMeTests : IAsyncLifetime
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

    private readonly PasswordHasher _passwordHasher = new();
    private readonly JwtTokenGenerator _jwtTokenGenerator;
    private readonly BatteryDbContext _context;
    private readonly AuthService _authService;
    private readonly List<long> _createdUserIds = new();

    public AdversarialRefreshTokenAndMeTests()
    {
        _jwtTokenGenerator = new JwtTokenGenerator(Options.Create(_jwtSettings));

        var optionsBuilder = new DbContextOptionsBuilder<BatteryDbContext>();
        optionsBuilder.UseNpgsql(ConnectionString)
                      .UseSnakeCaseNamingConvention();
        _context = new BatteryDbContext(optionsBuilder.Options);

        _authService = new AuthService(_context, _passwordHasher, _jwtTokenGenerator);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdUserIds.Count > 0)
        {
            try
            {
                var usersToDelete = await _context.Users
                    .Include(u => u.UserRoles)
                    .Include(u => u.Customer)
                    .Where(u => _createdUserIds.Contains(u.UserId))
                    .ToListAsync();

                foreach (var u in usersToDelete)
                {
                    if (u.Customer != null)
                    {
                        _context.Customers.Remove(u.Customer);
                    }
                    _context.UserRoles.RemoveRange(u.UserRoles);
                    _context.Users.Remove(u);
                }

                await _context.SaveChangesAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        await _context.DisposeAsync();
    }

    private string GenerateRandomSuffix() => Guid.NewGuid().ToString("N")[..8];

    private (AuthController Controller, DefaultHttpContext HttpContext, CurrentUserService CurrentUserService) CreateRealController()
    {
        var httpContextAccessor = new HttpContextAccessor();
        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext = httpContext;

        var currentUserService = new CurrentUserService(httpContextAccessor);
        var controller = new AuthController(_authService, currentUserService, NullLogger<AuthController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return (controller, httpContext, currentUserService);
    }

    private async Task<User> SeedActiveUserAsync(string prefix, string role = RoleType.User)
    {
        var suffix = GenerateRandomSuffix();
        var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == role);
        if (defaultRole == null)
        {
            defaultRole = new Role { Name = role, Description = $"Role {role}" };
            _context.Roles.Add(defaultRole);
            await _context.SaveChangesAsync();
        }

        var initialRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        var user = new User
        {
            Username = $"{prefix}_{suffix}",
            FullName = $"Test User {suffix}",
            Email = $"{prefix}_{suffix}@test.vn",
            Phone = $"097{Random.Shared.Next(1000000, 9999999)}",
            PasswordHash = _passwordHasher.HashPassword("P@ssword123"),
            IsActive = true,
            RefreshToken = initialRefreshToken,
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.UserRoles.Add(new UserRole
        {
            User = user,
            Role = defaultRole,
            AssignedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        _createdUserIds.Add(user.UserId);
        return user;
    }

    // =========================================================================
    // SECTION 1: DUAL-CHANNEL REFRESH TOKEN TESTS
    // =========================================================================

    [Fact]
    public async Task RefreshToken_ChannelA_CookieOnly_WithEmptyJsonBody_Returns200OkAndSetsCookie()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_cookie_empty");
        var (controller, httpContext, _) = CreateRealController();
        httpContext.Request.Headers.Append("Cookie", $"refreshToken={user.RefreshToken}");

        // Act: Sending empty JSON body DTO
        var emptyBody = new RefreshTokenRequestDto();
        var result = await controller.RefreshToken(emptyBody, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.NotEmpty(envelope.Data.AccessToken);
        Assert.NotEmpty(envelope.Data!.RefreshToken!);

        // Verify Set-Cookie header contains HttpOnly, SameSite=Strict, Secure
        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
        var setCookie = httpContext.Response.Headers["Set-Cookie"].ToString();
        var expectedCookieVal = Uri.EscapeDataString(envelope.Data.RefreshToken!);
        Assert.Contains($"refreshToken={expectedCookieVal}", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshToken_ChannelA_UrlEncodedCookieIncoming_VerifiesCookieDecoding()
    {
        // Arrange: Browser echoes back the exact Set-Cookie value which is URL-encoded
        var user = await SeedActiveUserAsync("adv_cookie_encoded");
        var (controller, httpContext, _) = CreateRealController();
        var urlEncodedToken = Uri.EscapeDataString(user.RefreshToken!);
        httpContext.Request.Headers.Append("Cookie", $"refreshToken={urlEncodedToken}");

        // Act
        var result = await controller.RefreshToken(null, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.NotEmpty(envelope.Data.AccessToken);
    }

    [Fact]
    public async Task RefreshToken_ChannelA_CookieOnly_WithNullBody_Returns200Ok()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_cookie_null");
        var (controller, httpContext, _) = CreateRealController();
        httpContext.Request.Headers.Append("Cookie", $"refreshToken={user.RefreshToken}");

        // Act: Null body
        var result = await controller.RefreshToken(null, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.NotEmpty(envelope.Data.AccessToken);
        Assert.NotEmpty(envelope.Data!.RefreshToken!);
    }

    [Fact]
    public async Task RefreshToken_ChannelB_JsonBodyOnly_NoCookie_Returns200Ok()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_body_only");
        var (controller, httpContext, _) = CreateRealController();
        // No cookie header set

        // Act: Send token in body
        var body = new RefreshTokenRequestDto { RefreshToken = user.RefreshToken };
        var result = await controller.RefreshToken(body, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.NotEmpty(envelope.Data.AccessToken);
        Assert.NotEmpty(envelope.Data!.RefreshToken!);
        Assert.Equal(user.UserId, envelope.Data.UserId);

        // Response should also set cookie for subsequent browser requests
        Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task RefreshToken_ChannelPrecedence_CookieOverridesBody()
    {
        // Arrange: User1 in cookie, User2 in body
        var user1 = await SeedActiveUserAsync("adv_user1");
        var user2 = await SeedActiveUserAsync("adv_user2");
        var (controller, httpContext, _) = CreateRealController();

        httpContext.Request.Headers.Append("Cookie", $"refreshToken={user1.RefreshToken}");
        var body = new RefreshTokenRequestDto { RefreshToken = user2.RefreshToken };

        // Act
        var result = await controller.RefreshToken(body, CancellationToken.None);

        // Assert: User1 (from Cookie) must be authenticated, NOT user2
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(user1.UserId, envelope.Data!.UserId);
        Assert.Equal(user1.Username, envelope.Data.Username);
    }

    [Fact]
    public async Task RefreshToken_EmptyCookie_FallsBackToValidBody_Returns200Ok()
    {
        // Arrange: Cookie exists but is empty string
        var user = await SeedActiveUserAsync("adv_empty_cookie");
        var (controller, httpContext, _) = CreateRealController();
        httpContext.Request.Headers.Append("Cookie", "refreshToken=");

        var body = new RefreshTokenRequestDto { RefreshToken = user.RefreshToken };

        // Act: Should fall back to body
        var result = await controller.RefreshToken(body, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(user.UserId, envelope.Data!.UserId);
    }

    [Fact]
    public async Task RefreshToken_InvalidCookie_DoesNotFallBackToValidBody_Returns401Unauthorized()
    {
        // Arrange: Cookie has invalid token, body has valid token
        var user = await SeedActiveUserAsync("adv_inv_cookie");
        var (controller, httpContext, _) = CreateRealController();
        httpContext.Request.Headers.Append("Cookie", "refreshToken=invalid_token_12345");

        var body = new RefreshTokenRequestDto { RefreshToken = user.RefreshToken };

        // Act: Priority 1 (Cookie) was provided, so it must NOT secretly ignore it and switch to body
        var result = await controller.RefreshToken(body, CancellationToken.None);

        // Assert
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("Invalid or expired refresh token.", envelope.Errors[0]);
    }

    [Fact]
    public async Task RefreshToken_DeactivatedUser_Returns401Unauthorized()
    {
        // Arrange: Seed user and then deactivate
        var user = await SeedActiveUserAsync("adv_deact_ref");
        user.IsActive = false;
        await _context.SaveChangesAsync();

        var (controller, _, _) = CreateRealController();
        var body = new RefreshTokenRequestDto { RefreshToken = user.RefreshToken };

        // Act
        var result = await controller.RefreshToken(body, CancellationToken.None);

        // Assert
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("User account is inactive.", envelope.Errors[0]);
    }

    [Fact]
    public async Task RefreshToken_MissingInBoth_Returns400BadRequest()
    {
        // Arrange
        var (controller, _, _) = CreateRealController();

        // 1. null request
        var res1 = await controller.RefreshToken(null, CancellationToken.None);
        var bad1 = Assert.IsType<BadRequestObjectResult>(res1.Result);
        var env1 = Assert.IsType<ApiResponse<AuthResponseDto>>(bad1.Value);
        Assert.False(env1.Success);
        Assert.Contains("Refresh token is required.", env1.Errors[0]);

        // 2. empty request object
        var res2 = await controller.RefreshToken(new RefreshTokenRequestDto(), CancellationToken.None);
        var bad2 = Assert.IsType<BadRequestObjectResult>(res2.Result);
        var env2 = Assert.IsType<ApiResponse<AuthResponseDto>>(bad2.Value);
        Assert.False(env2.Success);

        // 3. whitespace token
        var res3 = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = "    " }, CancellationToken.None);
        var bad3 = Assert.IsType<BadRequestObjectResult>(res3.Result);
        var env3 = Assert.IsType<ApiResponse<AuthResponseDto>>(bad3.Value);
        Assert.False(env3.Success);
    }

    [Fact]
    public async Task RefreshToken_ExpiredToken_Returns401Unauthorized()
    {
        // Arrange: Seed user with expired token
        var user = await SeedActiveUserAsync("adv_expired");
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-10);
        await _context.SaveChangesAsync();

        var (controller, httpContext, _) = CreateRealController();
        httpContext.Request.Headers.Append("Cookie", $"refreshToken={user.RefreshToken}");

        // Act
        var result = await controller.RefreshToken(null, CancellationToken.None);

        // Assert
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("Invalid or expired refresh token.", envelope.Errors[0]);
    }

    [Fact]
    public async Task RefreshToken_TamperedOrBogusToken_Returns401Unauthorized()
    {
        // Arrange
        var (controller, httpContext, _) = CreateRealController();
        httpContext.Request.Headers.Append("Cookie", "refreshToken=completely_fake_tampered_token_9876543210");

        // Act
        var result = await controller.RefreshToken(null, CancellationToken.None);

        // Assert
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("Invalid or expired refresh token.", envelope.Errors[0]);
    }

    [Fact]
    public async Task RefreshToken_ExtremelyLongOrMaliciousInput_Returns401UnauthorizedWithoutCrash()
    {
        // Arrange: Attacker sends 10,000 char token or SQL injection payload
        var (controller, _, _) = CreateRealController();
        var maliciousTokens = new[]
        {
            new string('A', 5000),
            "'; DROP TABLE users; --",
            "' OR '1'='1' --",
            ";;;;;;",
            "token1;token2"
        };

        foreach (var badToken in maliciousTokens)
        {
            var result = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = badToken }, CancellationToken.None);
            var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
            var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
            Assert.False(envelope.Success);
        }
    }

    // =========================================================================
    // SECTION 2: TOKEN ROTATION REPLAY BEHAVIOR & ANALYSIS
    // =========================================================================

    [Fact]
    public async Task RefreshToken_RotationReplay_DocumentsExactReplayAndPruningBehavior()
    {
        // Arrange: User with initial token T0
        var user = await SeedActiveUserAsync("adv_replay");
        var token0 = user.RefreshToken!;
        var (controller, _, _) = CreateRealController();

        // 1. Initial Refresh with T0 -> returns T1
        var res1 = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = token0 }, CancellationToken.None);
        var ok1 = Assert.IsType<OkObjectResult>(res1.Result);
        var env1 = Assert.IsType<ApiResponse<AuthResponseDto>>(ok1.Value);
        var token1 = env1.Data!.RefreshToken!;
        Assert.NotEqual(token0, token1);

        // 2. Replay #1: Presenting T0 again.
        // Under current multi-client design (holds 3 tokens), T0 is still in history:
        var replay1 = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = token0 }, CancellationToken.None);
        var okReplay1 = Assert.IsType<OkObjectResult>(replay1.Result);
        var envReplay1 = Assert.IsType<ApiResponse<AuthResponseDto>>(okReplay1.Value);
        var token2 = envReplay1.Data!.RefreshToken!;
        Assert.True(envReplay1.Success);

        // 3. Replay #2: Presenting T0 a third time:
        var replay2 = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = token0 }, CancellationToken.None);
        var okReplay2 = Assert.IsType<OkObjectResult>(replay2.Result);
        var envReplay2 = Assert.IsType<ApiResponse<AuthResponseDto>>(okReplay2.Value);
        var token3 = envReplay2.Data!.RefreshToken!;
        Assert.True(envReplay2.Success);

        // At this point, tokens in DB were [T0, T1, T2, T3].
        // Because max retained is 3, tokensList.TakeLast(3) prunes T0: [T1, T2, T3].
        var userInDb = await _context.Users.FindAsync(user.UserId);
        Assert.NotNull(userInDb);
        Assert.DoesNotContain(token0, userInDb.RefreshToken);

        // 4. Replay #3: Presenting T0 a fourth time -> MUST BE REJECTED with 401 Unauthorized!
        var replay3 = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = token0 }, CancellationToken.None);
        var unauthReplay3 = Assert.IsType<UnauthorizedObjectResult>(replay3.Result);
        var envReplay3 = Assert.IsType<ApiResponse<AuthResponseDto>>(unauthReplay3.Value);
        Assert.False(envReplay3.Success);
        Assert.Contains("Invalid or expired refresh token.", envReplay3.Errors[0]);

        // 5. Verify that the latest token (T3) remains completely valid and usable
        var validRefresh = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = token3 }, CancellationToken.None);
        var okValid = Assert.IsType<OkObjectResult>(validRefresh.Result);
        var envValid = Assert.IsType<ApiResponse<AuthResponseDto>>(okValid.Value);
        Assert.True(envValid.Success);
    }

    // =========================================================================
    // SECTION 3: GET /api/auth/me ENDPOINT TESTS
    // =========================================================================

    [Fact]
    public async Task GetCurrentUser_ValidBearerToken_ReturnsProfileAndRoles()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_me_valid", RoleType.StoreManager);
        var (controller, httpContext, _) = CreateRealController();

        // Create JWT Access Token with user claims
        var token = _jwtTokenGenerator.GenerateAccessToken(user, new[] { RoleType.StoreManager });
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Inject ClaimsPrincipal into HttpContext
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"));

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(user.UserId, envelope.Data.UserId);
        Assert.Equal(user.UserId, envelope.Data.Id); // Verify .id alias
        Assert.Equal(user.Username, envelope.Data.Username);
        Assert.Equal(user.FullName, envelope.Data.FullName);
        Assert.Equal(user.Email, envelope.Data.Email);
        Assert.Equal(user.Phone, envelope.Data.Phone);
        Assert.True(envelope.Data.IsActive);
        Assert.Contains(RoleType.StoreManager, envelope.Data.Roles);
    }

    [Fact]
    public async Task GetCurrentUser_ValidTokenWithSubClaimOnly_ResolvesUserId()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_me_sub");
        var (controller, httpContext, _) = CreateRealController();

        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", user.UserId.ToString()),
            new Claim("role", "User")
        }, "Bearer"));

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(user.UserId, envelope.Data!.UserId);
    }

    [Fact]
    public async Task GetCurrentUser_MissingToken_Returns401Unauthorized()
    {
        // Arrange: No user claims on HttpContext
        var (controller, _, _) = CreateRealController();

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("User is not authenticated.", envelope.Errors[0]);
    }

    [Fact]
    public async Task GetCurrentUser_NonNumericOrMalformedUserIdClaim_Returns401Unauthorized()
    {
        // Arrange: ClaimsPrincipal contains non-numeric identifier
        var (controller, httpContext, _) = CreateRealController();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "not_a_valid_long_id")
        }, "Bearer"));

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("User is not authenticated.", envelope.Errors[0]);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUserDeletedFromDb_Returns404NotFound()
    {
        // Arrange: Token claims valid user id 999999999 that doesn't exist in DB
        var (controller, httpContext, _) = CreateRealController();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999999999")
        }, "Bearer"));

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(notFound.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task GetCurrentUser_WithMultipleRoles_ReturnsAllRolesInProfile()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_me_multirole", RoleType.Admin);
        var warehouseRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleType.WarehouseStaff);
        if (warehouseRole == null)
        {
            warehouseRole = new Role { Name = RoleType.WarehouseStaff, Description = "Warehouse" };
            _context.Roles.Add(warehouseRole);
            await _context.SaveChangesAsync();
        }

        _context.UserRoles.Add(new UserRole
        {
            User = user,
            Role = warehouseRole,
            AssignedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var (controller, httpContext, _) = CreateRealController();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Role, RoleType.Admin),
            new Claim(ClaimTypes.Role, RoleType.WarehouseStaff)
        }, "Bearer"));

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Contains(RoleType.Admin, envelope.Data.Roles);
        Assert.Contains(RoleType.WarehouseStaff, envelope.Data.Roles);
    }

    [Fact]
    public async Task GetCurrentUser_InactiveUser_ReturnsProfileWithIsActiveFalse()
    {
        // Arrange
        var user = await SeedActiveUserAsync("adv_me_inactive");
        user.IsActive = false;
        await _context.SaveChangesAsync();

        var (controller, httpContext, _) = CreateRealController();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString())
        }, "Bearer"));

        // Act
        var result = await controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.False(envelope.Data.IsActive);
    }
}
