using System.Security.Claims;
using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Domain.Exceptions;
using BatteryEC.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEC.UnitTests;

public class AuthControllerTests
{
    private class FakeAuthService : IAuthService
    {
        public Func<RegisterRequestDto, Task<AuthResponseDto>>? OnRegister { get; set; }
        public Func<LoginRequestDto, Task<AuthResponseDto>>? OnLogin { get; set; }
        public Func<string?, Task<AuthResponseDto>>? OnRefreshToken { get; set; }
        public Func<long, Task<UserProfileDto>>? OnGetCurrentUser { get; set; }
        public Func<string?, long?, Task>? OnLogout { get; set; }

        public Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
        {
            if (OnRegister != null) return OnRegister(request);
            return Task.FromResult(new AuthResponseDto
            {
                UserId = 1,
                Username = request.Username ?? "user",
                AccessToken = "fake_access_token",
                RefreshToken = "fake_refresh_token"
            });
        }

        public Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
        {
            if (OnLogin != null) return OnLogin(request);
            return Task.FromResult(new AuthResponseDto
            {
                UserId = 1,
                Username = request.GetResolvedIdentifier(),
                AccessToken = "fake_access_token",
                RefreshToken = "fake_refresh_token"
            });
        }

        public Task<AuthResponseDto> RefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken = default)
        {
            if (OnRefreshToken != null) return OnRefreshToken(refreshToken);
            return Task.FromResult(new AuthResponseDto
            {
                UserId = 1,
                Username = "refreshed_user",
                AccessToken = "new_access_token",
                RefreshToken = "new_refresh_token"
            });
        }

        public Task<UserProfileDto> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
        {
            if (OnGetCurrentUser != null) return OnGetCurrentUser(userId);
            return Task.FromResult(new UserProfileDto
            {
                UserId = userId,
                Username = "profile_user",
                FullName = "Profile User",
                Roles = new List<string> { "User" }
            });
        }

        public Task LogoutAsync(string? refreshToken, long? userId = null, CancellationToken cancellationToken = default)
        {
            if (OnLogout != null) return OnLogout(refreshToken, userId);
            return Task.CompletedTask;
        }
    }

    private class FakeCurrentUserService : ICurrentUserService
    {
        public long? UserId { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private (AuthController Controller, DefaultHttpContext HttpContext, FakeAuthService AuthService, FakeCurrentUserService CurrentUserService) CreateController()
    {
        var authService = new FakeAuthService();
        var currentUserService = new FakeCurrentUserService();
        var controller = new AuthController(authService, currentUserService, NullLogger<AuthController>.Instance);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return (controller, httpContext, authService, currentUserService);
    }

    [Fact]
    public async Task Register_ValidRequest_ReturnsOkWithTokensAndSetsCookie()
    {
        var (controller, context, _, _) = CreateController();
        var request = new RegisterRequestDto
        {
            Username = "valid_user",
            FullName = "Valid User",
            Phone = "0987654321",
            Password = "P@ssword123",
            Email = "valid@test.vn"
        };

        var result = await controller.Register(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("fake_access_token", envelope.Data.AccessToken);

        // Verify Set-Cookie header
        Assert.True(context.Response.Headers.ContainsKey("Set-Cookie"));
        var cookieHeader = context.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("refreshToken=fake_refresh_token", cookieHeader);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_MissingFields_ReturnsBadRequest()
    {
        var (controller, _, _, _) = CreateController();
        var request = new RegisterRequestDto
        {
            Username = "missing_fields",
            FullName = "", // Missing
            Phone = "0987654321",
            Password = "P@ssword123"
        };

        var result = await controller.Register(request, CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequest.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsOkAndSetsCookie()
    {
        var (controller, context, _, _) = CreateController();
        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = "admin",
            Password = "P@ssword123"
        };

        var result = await controller.Login(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("fake_access_token", envelope.Data.AccessToken);

        var cookieHeader = context.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("refreshToken=fake_refresh_token", cookieHeader);
    }

    [Fact]
    public async Task Login_EmptyCredentials_ReturnsBadRequest()
    {
        var (controller, _, _, _) = CreateController();
        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = "",
            Password = ""
        };

        var result = await controller.Login(request, CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequest.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var (controller, _, authService, _) = CreateController();
        authService.OnLogin = _ => throw new UnauthorizedAccessException("Invalid credentials.");

        var request = new LoginRequestDto
        {
            UsernameOrPhoneOrEmail = "admin",
            Password = "WrongPassword"
        };

        var result = await controller.Login(request, CancellationToken.None);
        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("Invalid credentials.", envelope.Errors[0]);
    }

    [Fact]
    public async Task RefreshToken_WithCookieChannel_ReturnsOk()
    {
        var (controller, context, _, _) = CreateController();
        context.Request.Headers.Append("Cookie", "refreshToken=cookie_token_123");

        var result = await controller.RefreshToken(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("new_access_token", envelope.Data.AccessToken);
    }

    [Fact]
    public async Task RefreshToken_WithJsonBodyChannel_ReturnsOk()
    {
        var (controller, context, _, _) = CreateController();
        var request = new RefreshTokenRequestDto { RefreshToken = "body_token_456" };

        var result = await controller.RefreshToken(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("new_access_token", envelope.Data.AccessToken);
    }

    [Fact]
    public async Task RefreshToken_WhenBothChannelsMissing_ReturnsBadRequest()
    {
        var (controller, _, _, _) = CreateController();

        var result = await controller.RefreshToken(new RefreshTokenRequestDto { RefreshToken = null }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(badRequest.Value);
        Assert.False(envelope.Success);
        Assert.Contains("Refresh token is required.", envelope.Errors[0]);
    }

    [Fact]
    public async Task RefreshToken_InvalidToken_ReturnsUnauthorized()
    {
        var (controller, context, authService, _) = CreateController();
        context.Request.Headers.Append("Cookie", "refreshToken=bogus_token");
        authService.OnRefreshToken = _ => throw new UnauthorizedAccessException("Expired token.");

        var result = await controller.RefreshToken(null, CancellationToken.None);

        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AuthResponseDto>>(unauth.Value);
        Assert.False(envelope.Success);
        Assert.Contains("Expired token.", envelope.Errors[0]);
    }

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticatedViaService_ReturnsProfile()
    {
        var (controller, _, _, currentUserService) = CreateController();
        currentUserService.UserId = 42;

        var result = await controller.GetCurrentUser(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(42L, envelope.Data.UserId);
        Assert.Equal("profile_user", envelope.Data.Username);
    }

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticatedViaClaims_ReturnsProfile()
    {
        var (controller, context, _, currentUserService) = CreateController();
        currentUserService.UserId = null; // not in service
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99")
        }, "Bearer"));

        var result = await controller.GetCurrentUser(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(99L, envelope.Data.UserId);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var (controller, _, _, currentUserService) = CreateController();
        currentUserService.UserId = null;

        var result = await controller.GetCurrentUser(CancellationToken.None);

        var unauth = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(unauth.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task GetCurrentUser_WhenNotFoundInDb_ReturnsNotFound()
    {
        var (controller, _, authService, currentUserService) = CreateController();
        currentUserService.UserId = 12345;
        authService.OnGetCurrentUser = id => throw new EntityNotFoundException("User", id);

        var result = await controller.GetCurrentUser(CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<UserProfileDto>>(notFound.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task Logout_WithCookieToken_RevokesTokenAndDeletesCookie()
    {
        var (controller, context, authService, _) = CreateController();
        context.Request.Headers["Cookie"] = "refreshToken=valid_refresh_token";

        string? capturedToken = null;
        authService.OnLogout = (token, userId) =>
        {
            capturedToken = token;
            return Task.CompletedTask;
        };

        var actionResult = await controller.Logout(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("valid_refresh_token", capturedToken);
        Assert.True(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task Logout_WithRequestBodyToken_RevokesTokenAndDeletesCookie()
    {
        var (controller, context, authService, _) = CreateController();
        var request = new RefreshTokenRequestDto { RefreshToken = "body_refresh_token" };

        string? capturedToken = null;
        authService.OnLogout = (token, userId) =>
        {
            capturedToken = token;
            return Task.CompletedTask;
        };

        var actionResult = await controller.Logout(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("body_refresh_token", capturedToken);
    }
}
