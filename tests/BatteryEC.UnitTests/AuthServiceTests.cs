using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Exceptions;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Security;
using BatteryEC.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEC.UnitTests;

public class AuthServiceTests : IAsyncLifetime
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

    public AuthServiceTests()
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

    [Fact]
    public async Task RegisterAsync_WithValidDetails_CreatesUserAndReturnsTokens()
    {
        var suffix = GenerateRandomSuffix();
        var request = new RegisterRequestDto
        {
            Username = $"reg_user_{suffix}",
            FullName = $"Test User {suffix}",
            Email = $"reg_{suffix}@test.vn",
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        };

        var response = await _authService.RegisterAsync(request);
        _createdUserIds.Add(response.UserId);

        Assert.True(response.UserId > 0);
        Assert.Equal(request.Username, response.Username);
        Assert.Equal(request.Email, response.Email);
        Assert.Equal(request.Phone, response.Phone);
        Assert.NotEmpty(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Contains(RoleType.User, response.Roles);
        Assert.NotNull(response.User);
        Assert.Equal(response.UserId, response.User.UserId);

        // Verify entity in DB
        var userInDb = await _context.Users.FindAsync(response.UserId);
        Assert.NotNull(userInDb);
        Assert.True(userInDb.IsActive);
        Assert.True(_passwordHasher.VerifyPassword("P@ssword123", userInDb.PasswordHash));
        Assert.NotNull(userInDb.RefreshToken);
        Assert.True(userInDb.RefreshTokenExpiryTime > DateTime.UtcNow);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateUsername_ThrowsArgumentException()
    {
        var suffix = GenerateRandomSuffix();
        var phone = $"098{Random.Shared.Next(1000000, 9999999)}";
        var request = new RegisterRequestDto
        {
            Username = $"dup_user_{suffix}",
            FullName = $"First User {suffix}",
            Email = $"dup_{suffix}@test.vn",
            Phone = phone,
            Password = "P@ssword123"
        };

        var firstResp = await _authService.RegisterAsync(request);
        _createdUserIds.Add(firstResp.UserId);

        var duplicateRequest = new RegisterRequestDto
        {
            Username = request.Username, // same username
            FullName = "Another User",
            Email = $"different_{suffix}@test.vn",
            Phone = $"097{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(duplicateRequest));
        Assert.Contains("already taken", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicatePhone_ThrowsArgumentException()
    {
        var suffix = GenerateRandomSuffix();
        var phone = $"098{Random.Shared.Next(1000000, 9999999)}";
        var request = new RegisterRequestDto
        {
            Username = $"ph_user_{suffix}",
            FullName = $"Phone User {suffix}",
            Email = $"ph_{suffix}@test.vn",
            Phone = phone,
            Password = "P@ssword123"
        };

        var firstResp = await _authService.RegisterAsync(request);
        _createdUserIds.Add(firstResp.UserId);

        var duplicateRequest = new RegisterRequestDto
        {
            Username = $"other_user_{suffix}",
            FullName = "Another User",
            Email = $"other_{suffix}@test.vn",
            Phone = phone, // duplicate phone
            Password = "P@ssword123"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(duplicateRequest));
        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ThrowsArgumentException()
    {
        var suffix = GenerateRandomSuffix();
        var email = $"email_{suffix}@test.vn";
        var request = new RegisterRequestDto
        {
            Username = $"em_user_{suffix}",
            FullName = $"Email User {suffix}",
            Email = email,
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        };

        var firstResp = await _authService.RegisterAsync(request);
        _createdUserIds.Add(firstResp.UserId);

        var duplicateRequest = new RegisterRequestDto
        {
            Username = $"other_em_user_{suffix}",
            FullName = "Another User",
            Email = email, // duplicate email
            Phone = $"097{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(duplicateRequest));
        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterAsync_WithShortPassword_ThrowsArgumentException()
    {
        var request = new RegisterRequestDto
        {
            Username = $"short_pwd_{GenerateRandomSuffix()}",
            FullName = "Short Pwd User",
            Email = "short@test.vn",
            Phone = "0981112233",
            Password = "123" // < 6 chars
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("at least 6 characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_WithFlexibleIdentifiers_AuthenticatesSuccessfully()
    {
        var suffix = GenerateRandomSuffix();
        var phone = $"098{Random.Shared.Next(1000000, 9999999)}";
        var email = $"flex_{suffix}@test.vn";
        var username = $"flex_user_{suffix}";
        var password = "P@ssword123";

        var regResp = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = username,
            FullName = $"Flex User {suffix}",
            Email = email,
            Phone = phone,
            Password = password
        });
        _createdUserIds.Add(regResp.UserId);

        // 1. Login via Username
        var loginUser = await _authService.LoginAsync(new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = username,
            Password = password
        });
        Assert.Equal(regResp.UserId, loginUser.UserId);
        Assert.NotEmpty(loginUser.AccessToken);
        Assert.NotNull(loginUser.RefreshToken);
        Assert.NotEmpty(loginUser.RefreshToken);

        // 2. Login via Phone
        var loginPhone = await _authService.LoginAsync(new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = phone,
            Password = password
        });
        Assert.Equal(regResp.UserId, loginPhone.UserId);

        // 3. Login via Email
        var loginEmail = await _authService.LoginAsync(new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = email,
            Password = password
        });
        Assert.Equal(regResp.UserId, loginEmail.UserId);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ThrowsUnauthorizedAccessException()
    {
        var suffix = GenerateRandomSuffix();
        var username = $"wrong_pwd_{suffix}";
        var regResp = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = username,
            FullName = "Wrong Pwd User",
            Email = $"wp_{suffix}@test.vn",
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(regResp.UserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = username,
            Password = "IncorrectPassword!"
        }));
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = "completely_fictional_user_xyz_99999",
            Password = "P@ssword123"
        }));
    }

    [Fact]
    public async Task LoginAsync_WithDeactivatedUser_ThrowsUnauthorizedAccessException()
    {
        var suffix = GenerateRandomSuffix();
        var username = $"deact_user_{suffix}";
        var regResp = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = username,
            FullName = "Deactivated User",
            Email = $"deact_{suffix}@test.vn",
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(regResp.UserId);

        // Deactivate in DB
        var user = await _context.Users.FindAsync(regResp.UserId);
        Assert.NotNull(user);
        user.IsActive = false;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = username,
            Password = "P@ssword123"
        }));
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_RotatesTokensSuccessfully()
    {
        var suffix = GenerateRandomSuffix();
        var regResp = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = $"refresh_user_{suffix}",
            FullName = "Refresh User",
            Email = $"ref_{suffix}@test.vn",
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(regResp.UserId);

        var firstRefreshToken = regResp.RefreshToken;
        Assert.NotNull(firstRefreshToken);

        // Refresh tokens
        var refreshedResp = await _authService.RefreshTokenAsync(firstRefreshToken);

        Assert.Equal(regResp.UserId, refreshedResp.UserId);
        Assert.NotEmpty(refreshedResp.AccessToken);
        Assert.NotNull(refreshedResp.RefreshToken);
        Assert.NotEmpty(refreshedResp.RefreshToken);
        Assert.NotEqual(firstRefreshToken, refreshedResp.RefreshToken);

        // Refreshing with the newly rotated token succeeds as well
        var secondRefresh = await _authService.RefreshTokenAsync(refreshedResp.RefreshToken);
        Assert.Equal(regResp.UserId, secondRefresh.UserId);
        Assert.NotEmpty(secondRefresh.AccessToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithExpiredToken_ThrowsUnauthorizedAccessException()
    {
        var suffix = GenerateRandomSuffix();
        var regResp = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = $"exp_user_{suffix}",
            FullName = "Expired Token User",
            Email = $"exp_{suffix}@test.vn",
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(regResp.UserId);

        // Manually expire token in DB
        var user = await _context.Users.FindAsync(regResp.UserId);
        Assert.NotNull(user);
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-5);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshTokenAsync(regResp.RefreshToken));
    }

    [Fact]
    public async Task RefreshTokenAsync_WithBogusToken_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _authService.RefreshTokenAsync("non_existent_token_1234567890"));
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithValidId_ReturnsUserProfile()
    {
        var suffix = GenerateRandomSuffix();
        var regResp = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Username = $"me_user_{suffix}",
            FullName = "Me User Profile",
            Email = $"me_{suffix}@test.vn",
            Phone = $"098{Random.Shared.Next(1000000, 9999999)}",
            Password = "P@ssword123"
        });
        _createdUserIds.Add(regResp.UserId);

        var profile = await _authService.GetCurrentUserAsync(regResp.UserId);

        Assert.NotNull(profile);
        Assert.Equal(regResp.UserId, profile.UserId);
        Assert.Equal(regResp.UserId, profile.Id);
        Assert.Equal($"me_user_{suffix}", profile.Username);
        Assert.Equal("Me User Profile", profile.FullName);
        Assert.Equal($"me_{suffix}@test.vn", profile.Email);
        Assert.Contains(RoleType.User, profile.Roles);
        Assert.True(profile.IsActive);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithNonExistentId_ThrowsEntityNotFoundException()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _authService.GetCurrentUserAsync(99999999L));
    }
}
