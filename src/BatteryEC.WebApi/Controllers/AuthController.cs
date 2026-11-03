using System.Security.Claims;
using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BatteryEC.WebApi.Controllers;

/// <summary>
/// Authentication and session controller providing registration, multi-identifier login,
/// dual-channel refresh token rotation, and current user identity introspection.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ICurrentUserService currentUserService,
        ILogger<AuthController> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers a new user account, assigns the default User role, and auto-links Customer if phone matches.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register(
        [FromBody] RegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            return BadRequest(ApiResponse<AuthResponseDto>.Fail("Request payload is required."));

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<AuthResponseDto>.Fail("Full name, phone, and password are required."));
        }

        try
        {
            var result = await _authService.RegisterAsync(request, cancellationToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(ApiResponse<AuthResponseDto>.Ok(result, "User registered successfully."));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Registration failed validation: {Message}", ex.Message);
            return BadRequest(ApiResponse<AuthResponseDto>.Fail(ex.Message));
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Registration failed domain rule: {Message}", ex.Message);
            return BadRequest(ApiResponse<AuthResponseDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Authenticates a user using Username, Phone number, or Email.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.GetResolvedIdentifier()) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<AuthResponseDto>.Fail("Username/phone/email and password are required."));
        }

        try
        {
            var result = await _authService.LoginAsync(request, cancellationToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Login successful."));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Login failed for identifier '{Identifier}': {Message}",
                request.GetResolvedIdentifier(), ex.Message);
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<AuthResponseDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Refreshes an access token using dual-channel refresh token (HttpOnly Cookie or JSON body).
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> RefreshToken(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequestDto? request,
        CancellationToken cancellationToken)
    {
        // Dual-channel token inspection:
        // Priority 1: HttpOnly Cookie 'refreshToken'
        var token = Request.Cookies["refreshToken"];

        // Priority 2: JSON Body 'refreshToken'
        if (string.IsNullOrWhiteSpace(token))
        {
            token = request?.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(ApiResponse<AuthResponseDto>.Fail("Refresh token is required."));
        }

        try
        {
            var result = await _authService.RefreshTokenAsync(token, cancellationToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Token refreshed successfully."));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Token refresh failed: {Message}", ex.Message);
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Returns current authenticated user's identity, roles, and profile information.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetCurrentUser(CancellationToken cancellationToken)
    {
        long? userId = _currentUserService.UserId;

        if (!userId.HasValue)
        {
            var subClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;

            if (long.TryParse(subClaim, out var parsedId))
            {
                userId = parsedId;
            }
        }

        if (!userId.HasValue)
        {
            return Unauthorized(ApiResponse<UserProfileDto>.Fail("User is not authenticated."));
        }

        try
        {
            var profile = await _authService.GetCurrentUserAsync(userId.Value, cancellationToken);
            return Ok(ApiResponse<UserProfileDto>.Ok(profile));
        }
        catch (EntityNotFoundException ex)
        {
            return NotFound(ApiResponse<UserProfileDto>.Fail(ex.Message));
        }
    }

    private void SetRefreshTokenCookie(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Transport Security flag (RFC 6265bis compliant)
            SameSite = SameSiteMode.Strict, // Strict CSRF mitigation
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/",
            IsEssential = true
        };

        Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
    }
}
