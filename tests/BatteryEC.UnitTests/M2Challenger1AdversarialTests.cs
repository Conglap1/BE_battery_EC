using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Exceptions;
using BatteryEC.Infrastructure.Persistence;
using BatteryEC.Infrastructure.Security;
using BatteryEC.Infrastructure.Services;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace BatteryEC.UnitTests;

/// <summary>
/// Milestone 2 Challenger 1: Adversarial Testing of Registration & Login Flows.
/// Tests executed empirically against live PostgreSQL database (battery-postgres).
/// Covers:
/// 1. Registration Duplicates (exact, case variation, phone formatting, whitespace, clean 400 vs 500 DB crashes)
/// 2. Customer Phone Auto-Linking (PostgreSQL trigger trg_users_link_customer_on_register verification, customer.user_id linkage, subsequent login/profile CustomerId)
/// 3. Flexible Login (Username, Phone, Email, Case Insensitivity, Wrong Password 401, Non-Existent User 401, Inactive User 401, Field Aliases)
/// 4. Password Security (PBKDF2 HMAC-SHA256 salt & hash validation, 0 plaintext passwords in PostgreSQL)
/// 5. Controller Layer Contract (HTTP Status Codes 200, 400, 401, HttpOnly Secure Strict Set-Cookie verification)
/// </summary>
public class M2Challenger1AdversarialTests : IAsyncLifetime
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
    private readonly ITestOutputHelper _output;

    // Track test entities for idempotent teardown
    private readonly List<long> _createdUserIds = new();
    private readonly List<long> _createdCustomerIds = new();

    public M2Challenger1AdversarialTests(ITestOutputHelper output)
    {
        _output = output;
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
        _context.ChangeTracker.Clear();

        // Teardown created users and custom test customers
        if (_createdUserIds.Count > 0)
        {
            try
            {
                var usersToDelete = await _context.Users
                    .Include(u => u.UserRoles)
                    .Where(u => _createdUserIds.Contains(u.UserId))
                    .ToListAsync();

                foreach (var user in usersToDelete)
                {
                    // Unlink any customer referencing this user
                    var linkedCustomers = await _context.Customers
                        .Where(c => c.UserId == user.UserId)
                        .ToListAsync();

                    foreach (var c in linkedCustomers)
                    {
                        c.UserId = null;
                    }
                    await _context.SaveChangesAsync();

                    _context.UserRoles.RemoveRange(user.UserRoles);
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Teardown users warning: {ex.Message}");
            }
        }

        if (_createdCustomerIds.Count > 0)
        {
            try
            {
                var customersToDelete = await _context.Customers
                    .Where(c => _createdCustomerIds.Contains(c.CustomerId))
                    .ToListAsync();

                if (customersToDelete.Count > 0)
                {
                    _context.Customers.RemoveRange(customersToDelete);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Teardown customers warning: {ex.Message}");
            }
        }

        await _context.DisposeAsync();
    }

    private AuthController CreateController(ICurrentUserService? currentUserService = null)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new AuthController(
            _authService,
            currentUserService ?? new FakeCurrentUserService(),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
        return controller;
    }

    #region 1. Adversarial Registration Duplicates & Validation

    [Fact]
    public async Task Register_DuplicateUsername_ExactMatch_ThrowsArgumentException_AndControllerReturns400()
    {
        // 'admin' is a seeded user in database
        var request = new RegisterRequestDto
        {
            Username = "admin",
            FullName = "Admin Impersonator",
            Email = "new_admin_unique@batterypartsstore.vn",
            Phone = "0988111222",
            Password = "P@ssword123!"
        };

        // Act & Assert AuthService
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("Username 'admin' is already taken", ex.Message);

        // Act & Assert Controller layer returns HTTP 400 (not 500)
        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("already taken", apiResponse.Errors[0]);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    [InlineData("aDmIn")]
    [InlineData("  admin  ")]
    public async Task Register_DuplicateUsername_CaseVariationsAndWhitespace_ThrowsArgumentException_AndControllerReturns400(string usernameVariation)
    {
        var request = new RegisterRequestDto
        {
            Username = usernameVariation,
            FullName = "Case Impersonator",
            Email = $"unique_{Guid.NewGuid():N}@test.vn",
            Phone = $"098{RandomNumberGenerator.GetInt32(1000000, 9999999)}",
            Password = "P@ssword123!"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("already taken", ex.Message, StringComparison.OrdinalIgnoreCase);

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("already taken", apiResponse.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_DuplicatePhone_ExactMatch_ThrowsArgumentException_AndControllerReturns400()
    {
        // 0901234567 is the seeded phone number for 'admin'
        var request = new RegisterRequestDto
        {
            Username = $"unique_user_{Guid.NewGuid():N}",
            FullName = "Duplicate Phone User",
            Email = $"unique_{Guid.NewGuid():N}@test.vn",
            Phone = "0901234567",
            Password = "P@ssword123!"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("Phone number '0901234567' is already registered", ex.Message);

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("already registered", apiResponse.Errors[0]);
    }

    [Theory]
    [InlineData(" 0901234567 ")]
    [InlineData("0901234567 ")]
    [InlineData(" 0901234567")]
    public async Task Register_DuplicatePhone_WithWhitespace_ThrowsArgumentException_AndControllerReturns400(string phoneVariation)
    {
        var request = new RegisterRequestDto
        {
            Username = $"unique_user_{Guid.NewGuid():N}",
            FullName = "Whitespace Phone User",
            Email = $"unique_{Guid.NewGuid():N}@test.vn",
            Phone = phoneVariation,
            Password = "P@ssword123!"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("already registered", ex.Message);

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("already registered", apiResponse.Errors[0]);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ExactMatch_ThrowsArgumentException_AndControllerReturns400()
    {
        // admin@batterypartsstore.vn is seeded admin email
        var request = new RegisterRequestDto
        {
            Username = $"unique_user_{Guid.NewGuid():N}",
            FullName = "Duplicate Email User",
            Email = "admin@batterypartsstore.vn",
            Phone = $"097{RandomNumberGenerator.GetInt32(1000000, 9999999)}",
            Password = "P@ssword123!"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("Email 'admin@batterypartsstore.vn' is already registered", ex.Message);

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("already registered", apiResponse.Errors[0]);
    }

    [Theory]
    [InlineData("ADMIN@BATTERYPARTSSTORE.VN")]
    [InlineData("Admin@BatteryPartsStore.vn")]
    [InlineData(" admin@batterypartsstore.vn ")]
    public async Task Register_DuplicateEmail_CaseVariationsAndWhitespace_ThrowsArgumentException_AndControllerReturns400(string emailVariation)
    {
        var request = new RegisterRequestDto
        {
            Username = $"unique_user_{Guid.NewGuid():N}",
            FullName = "Case Email User",
            Email = emailVariation,
            Phone = $"097{RandomNumberGenerator.GetInt32(1000000, 9999999)}",
            Password = "P@ssword123!"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegisterAsync(request));
        Assert.Contains("already registered", ex.Message);

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("already registered", apiResponse.Errors[0]);
    }

    [Theory]
    [InlineData(null, null, "Valid Pass", "Full Name")]
    [InlineData("", "", "Valid Pass", "Full Name")]
    [InlineData("user", null, "Valid Pass", "Full Name")]
    [InlineData("user", "", "Valid Pass", "Full Name")]
    [InlineData("user", "0912345678", null, "Full Name")]
    [InlineData("user", "0912345678", "", "Full Name")]
    [InlineData("user", "0912345678", "12345", "Full Name")] // password < 6 chars
    [InlineData("user", "0912345678", "Valid Pass", null)]
    [InlineData("user", "0912345678", "Valid Pass", "   ")]
    public async Task Register_InvalidPayloadFields_Returns400BadRequest(
        string? username, string? phone, string? password, string? fullName)
    {
        var request = new RegisterRequestDto
        {
            Username = username,
            Phone = phone ?? string.Empty,
            Password = password ?? "",
            FullName = fullName ?? ""
        };

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    #endregion

    #region 2. Customer Phone Auto-Linking (PostgreSQL Trigger Verification)

    [Fact]
    public async Task CustomerPhoneAutoLinking_WhenCustomerExistsWithoutUserId_TriggerCorrectlyLinksCustomerUserId()
    {
        // 1. Arrange: Create a dedicated unlinked customer record directly in PostgreSQL
        var testPhone = $"098{RandomNumberGenerator.GetInt32(1000000, 9999999)}";
        var unlinkedCustomer = new Customer
        {
            Phone = testPhone,
            FullName = "Khách Hàng Vãng Lai POS",
            TotalSpent = 1500000,
            UserId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.Customers.Add(unlinkedCustomer);
        await _context.SaveChangesAsync();
        _createdCustomerIds.Add(unlinkedCustomer.CustomerId);

        // Verify customer exists and user_id IS NULL in DB
        var initialCustomerInDb = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == unlinkedCustomer.CustomerId);
        Assert.NotNull(initialCustomerInDb);
        Assert.Null(initialCustomerInDb.UserId);

        // 2. Act: Register a new user with this EXACT phone number
        var username = $"autolink_{Guid.NewGuid():N}".Substring(0, 20);
        var registerRequest = new RegisterRequestDto
        {
            Username = username,
            FullName = "Khách Hàng Đã Tạo Tài Khoản",
            Phone = testPhone,
            Email = $"{username}@test.vn",
            Password = "P@ssword123!"
        };

        var authResult = await _authService.RegisterAsync(registerRequest);
        _createdUserIds.Add(authResult.UserId);

        // Clear EF Core local tracking cache to simulate independent subsequent requests
        _context.ChangeTracker.Clear();

        // 3. Assert: Direct empirical query against PostgreSQL database
        // The PostgreSQL trigger trg_users_link_customer_on_register MUST have updated customer.user_id
        var updatedCustomerInDb = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == unlinkedCustomer.CustomerId);

        Assert.NotNull(updatedCustomerInDb);
        Assert.NotNull(updatedCustomerInDb.UserId);
        Assert.Equal(authResult.UserId, updatedCustomerInDb.UserId.Value);
        _output.WriteLine($"Trigger successfully linked Customer ID {updatedCustomerInDb.CustomerId} to User ID {updatedCustomerInDb.UserId.Value}");

        // 4. Assert: Verify subsequent LoginAsync returns CustomerId properly
        var loginResponse = await _authService.LoginAsync(new LoginRequestDto
        {
            Username = username,
            Password = "P@ssword123!"
        });
        Assert.NotNull(loginResponse.User);
        Assert.Equal(unlinkedCustomer.CustomerId, loginResponse.User.CustomerId);

        // 5. Assert: Verify GetCurrentUserAsync returns CustomerId properly
        var profile = await _authService.GetCurrentUserAsync(authResult.UserId);
        Assert.Equal(unlinkedCustomer.CustomerId, profile.CustomerId);
    }

    [Fact]
    public async Task CustomerPhoneAutoLinking_SeededCustomer8_IsLinkedWhenUserRegistersWithSeedPhone()
    {
        // Seed customer 8 has phone '0908999888' and user_id IS NULL initially
        var customer8 = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == 8);
        Assert.NotNull(customer8);
        Assert.Equal("0908999888", customer8.Phone);

        // If previously linked by another test, reset user_id to null for test isolation
        if (customer8.UserId.HasValue)
        {
            customer8.UserId = null;
            await _context.SaveChangesAsync();
        }

        // Register user with customer 8's phone
        var username = $"cust8_{Guid.NewGuid():N}".Substring(0, 20);
        var registerRequest = new RegisterRequestDto
        {
            Username = username,
            FullName = "Nguyễn Thị Hoa Tài Khoản Mới",
            Phone = "0908999888",
            Email = $"{username}@test.vn",
            Password = "P@ssword123!"
        };

        var authResult = await _authService.RegisterAsync(registerRequest);
        _createdUserIds.Add(authResult.UserId);

        _context.ChangeTracker.Clear();

        // Verify DB linkage
        var refreshedCustomer8 = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == 8);

        Assert.NotNull(refreshedCustomer8);
        Assert.Equal(authResult.UserId, refreshedCustomer8.UserId);
    }

    [Fact]
    public async Task CustomerPhoneAutoLinking_WhenCustomerDoesNotExist_TriggerCreatesNewCustomerRow()
    {
        var brandNewPhone = $"099{RandomNumberGenerator.GetInt32(1000000, 9999999)}";
        var username = $"brandnew_{Guid.NewGuid():N}".Substring(0, 20);

        var registerRequest = new RegisterRequestDto
        {
            Username = username,
            FullName = "Brand New Customer",
            Phone = brandNewPhone,
            Email = $"{username}@test.vn",
            Password = "P@ssword123!"
        };

        var authResult = await _authService.RegisterAsync(registerRequest);
        _createdUserIds.Add(authResult.UserId);

        _context.ChangeTracker.Clear();

        // Verify that trigger created a corresponding customer row in PostgreSQL
        var createdCustomer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Phone == brandNewPhone);

        Assert.NotNull(createdCustomer);
        Assert.Equal(authResult.UserId, createdCustomer.UserId);
        Assert.Equal("Brand New Customer", createdCustomer.FullName);
        _createdCustomerIds.Add(createdCustomer.CustomerId);
    }

    #endregion

    #region 3. Flexible Multi-Identifier Login & Edge Cases

    [Fact]
    public async Task Login_ByExactUsername_Success()
    {
        var request = new LoginRequestDto
        {
            Username = "admin",
            Password = "P@ssword123"
        };

        var response = await _authService.LoginAsync(request);

        Assert.NotNull(response);
        Assert.Equal("admin", response.Username);
        Assert.Equal(1, response.UserId);
        Assert.Contains(RoleType.Admin, response.Roles);
        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
    }

    [Fact]
    public async Task Login_ByExactPhone_Success()
    {
        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = "0901234567",
            Password = "P@ssword123"
        };

        var response = await _authService.LoginAsync(request);

        Assert.NotNull(response);
        Assert.Equal("admin", response.Username);
        Assert.Equal("0901234567", response.Phone);
        Assert.Equal(1, response.UserId);
    }

    [Fact]
    public async Task Login_ByExactEmail_Success()
    {
        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = "admin@batterypartsstore.vn",
            Password = "P@ssword123"
        };

        var response = await _authService.LoginAsync(request);

        Assert.NotNull(response);
        Assert.Equal("admin", response.Username);
        Assert.Equal("admin@batterypartsstore.vn", response.Email);
        Assert.Equal(1, response.UserId);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    [InlineData("aDmIn")]
    public async Task Login_ByUsername_CaseInsensitive_Success(string usernameCase)
    {
        var request = new LoginRequestDto
        {
            Identifier = usernameCase,
            Password = "P@ssword123"
        };

        var response = await _authService.LoginAsync(request);

        Assert.NotNull(response);
        Assert.Equal("admin", response.Username);
        Assert.Equal(1, response.UserId);
    }

    [Theory]
    [InlineData("ADMIN@BATTERYPARTSSTORE.VN")]
    [InlineData("Admin@BatteryPartsStore.vn")]
    public async Task Login_ByEmail_CaseInsensitive_Success(string emailCase)
    {
        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = emailCase,
            Password = "P@ssword123"
        };

        var response = await _authService.LoginAsync(request);

        Assert.NotNull(response);
        Assert.Equal("admin", response.Username);
        Assert.Equal(1, response.UserId);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsUnauthorizedAccessException_AndControllerReturns401()
    {
        var request = new LoginRequestDto
        {
            Username = "admin",
            Password = "WrongPassword#2026!"
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));
        Assert.Equal("Invalid username or password.", ex.Message);

        var controller = CreateController();
        var actionResult = await controller.Login(request, CancellationToken.None);
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("Invalid username or password.", apiResponse.Errors[0]);
    }

    [Fact]
    public async Task Login_NonExistentIdentifier_ThrowsUnauthorizedAccessException_AndControllerReturns401()
    {
        var request = new LoginRequestDto
        {
            Username = "non_existent_user_999999",
            Password = "P@ssword123!"
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));
        Assert.Equal("Invalid username or password.", ex.Message);

        var controller = CreateController();
        var actionResult = await controller.Login(request, CancellationToken.None);
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("Invalid username or password.", apiResponse.Errors[0]);
    }

    [Fact]
    public async Task Login_InactiveUser_ThrowsUnauthorizedAccessException_AndControllerReturns401()
    {
        // 1. Arrange: Create an inactive user in database
        var username = $"inactive_{Guid.NewGuid():N}".Substring(0, 20);
        var inactiveUser = new User
        {
            Username = username,
            FullName = "Tài Khoản Bị Khóa",
            Email = $"{username}@test.vn",
            Phone = $"096{RandomNumberGenerator.GetInt32(1000000, 9999999)}",
            PasswordHash = _passwordHasher.HashPassword("P@ssword123!"),
            IsActive = false, // INACTIVE
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(inactiveUser);
        await _context.SaveChangesAsync();
        _createdUserIds.Add(inactiveUser.UserId);

        var request = new LoginRequestDto
        {
            Username = username,
            Password = "P@ssword123!"
        };

        // 2. Act & Assert AuthService
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));
        Assert.Equal("User account is inactive.", ex.Message);

        // 3. Act & Assert Controller returns 401
        var controller = CreateController();
        var actionResult = await controller.Login(request, CancellationToken.None);
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<AuthResponseDto>>(unauthorizedResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("User account is inactive.", apiResponse.Errors[0]);
    }

    [Theory]
    [InlineData("usernameOrPhoneOrEmail")]
    [InlineData("identifier")]
    [InlineData("username")]
    public async Task Login_SupportsAllThreeIdentifierPropertyAliases(string aliasType)
    {
        LoginRequestDto request = aliasType switch
        {
            "usernameOrPhoneOrEmail" => new LoginRequestDto { UsernameOrPhoneOrEmail = "admin", Password = "P@ssword123" },
            "identifier" => new LoginRequestDto { Identifier = "admin", Password = "P@ssword123" },
            "username" => new LoginRequestDto { Username = "admin", Password = "P@ssword123" },
            _ => throw new ArgumentException()
        };

        var response = await _authService.LoginAsync(request);
        Assert.NotNull(response);
        Assert.Equal("admin", response.Username);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData(null, "password")]
    [InlineData("admin", "")]
    [InlineData("admin", "   ")]
    [InlineData("admin", null)]
    public async Task Login_EmptyIdentifierOrPassword_Returns400BadRequest(string? identifier, string? password)
    {
        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = identifier ?? string.Empty,
            Password = password ?? ""
        };

        var controller = CreateController();
        var actionResult = await controller.Login(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    #endregion

    #region 4. Direct PostgreSQL Password Security Audit

    [Fact]
    public async Task PasswordAudit_AllSeededUsers_HavePBKDF2Hashes_AndZeroPlaintextPasswords()
    {
        // Seed users 1 through 7
        var users = await _context.Users
            .Where(u => u.UserId <= 7)
            .AsNoTracking()
            .ToListAsync();
        Assert.Equal(7, users.Count);

        foreach (var user in users)
        {
            // Verify password_hash is NOT plaintext
            Assert.NotEqual("P@ssword123", user.PasswordHash);
            Assert.NotEqual("admin", user.PasswordHash);
            Assert.NotEqual("password", user.PasswordHash);

            // Verify PBKDF2 format: {iterations}.{salt_base64}.{hash_base64}
            var parts = user.PasswordHash.Split('.');
            Assert.Equal(3, parts.Length);

            // Iterations must be 100,000
            Assert.True(int.TryParse(parts[0], out int iterations));
            Assert.Equal(100000, iterations);

            // Salt must be 16 bytes (Base64 length 24)
            byte[] salt = Convert.FromBase64String(parts[1]);
            Assert.Equal(16, salt.Length);

            // Hash must be 32 bytes (Base64 length 44)
            byte[] hash = Convert.FromBase64String(parts[2]);
            Assert.Equal(32, hash.Length);

            // Verify password verification succeeds with correct seed password
            bool verified = _passwordHasher.VerifyPassword("P@ssword123", user.PasswordHash);
            Assert.True(verified, $"Password verification failed for user '{user.Username}'");
        }
    }

    [Fact]
    public async Task PasswordAudit_NewlyRegisteredUser_HasCryptographicallySecurePBKDF2HashInDatabase()
    {
        var plaintextPassword = "MySecureAdversarialPassword!#2026";
        var username = $"sec_{Guid.NewGuid():N}".Substring(0, 20);

        var request = new RegisterRequestDto
        {
            Username = username,
            FullName = "Security Test User",
            Phone = $"095{RandomNumberGenerator.GetInt32(1000000, 9999999)}",
            Email = $"{username}@test.vn",
            Password = plaintextPassword
        };

        var authResult = await _authService.RegisterAsync(request);
        _createdUserIds.Add(authResult.UserId);

        _context.ChangeTracker.Clear();

        // Fetch freshly created user directly from PostgreSQL
        var userInDb = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == authResult.UserId);

        Assert.NotNull(userInDb);
        Assert.NotEqual(plaintextPassword, userInDb.PasswordHash);
        Assert.DoesNotContain(plaintextPassword, userInDb.PasswordHash);

        var parts = userInDb.PasswordHash.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("100000", parts[0]);

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] hash = Convert.FromBase64String(parts[2]);
        Assert.Equal(16, salt.Length);
        Assert.Equal(32, hash.Length);

        // Verify correct password matches, wrong password fails
        Assert.True(_passwordHasher.VerifyPassword(plaintextPassword, userInDb.PasswordHash));
        Assert.False(_passwordHasher.VerifyPassword("WrongPassword123", userInDb.PasswordHash));
    }

    #endregion

    #region 5. Controller Set-Cookie & Security Transport Tests

    [Fact]
    public async Task AuthController_Register_SetsHttpOnlySecureStrictCookie()
    {
        var username = $"cookie_reg_{Guid.NewGuid():N}".Substring(0, 20);
        var request = new RegisterRequestDto
        {
            Username = username,
            FullName = "Cookie Test User",
            Phone = $"094{RandomNumberGenerator.GetInt32(1000000, 9999999)}",
            Email = $"{username}@test.vn",
            Password = "P@ssword123!"
        };

        var controller = CreateController();
        var actionResult = await controller.Register(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);

        _createdUserIds.Add(response.Data!.UserId);

        // Check Set-Cookie header on response
        var setCookieHeader = controller.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("refreshToken=", setCookieHeader);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthController_Login_SetsHttpOnlySecureStrictCookie()
    {
        var request = new LoginRequestDto
        {
            Username = "admin",
            Password = "P@ssword123"
        };

        var controller = CreateController();
        var actionResult = await controller.Login(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var setCookieHeader = controller.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("refreshToken=", setCookieHeader);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    private class FakeCurrentUserService : ICurrentUserService
    {
        public long? UserId { get; set; } = 1;
        public string? UserName { get; set; } = "admin";
        public string? Email { get; set; } = "admin@batterypartsstore.vn";
        public IReadOnlyList<string> Roles { get; set; } = new List<string> { RoleType.Admin };
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
