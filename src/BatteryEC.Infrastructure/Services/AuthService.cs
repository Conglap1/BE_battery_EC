using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.DTOs.Auth;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Domain.Enums;
using BatteryEC.Domain.Exceptions;
using BatteryEC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly BatteryDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        BatteryDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _jwtTokenGenerator = jwtTokenGenerator ?? throw new ArgumentNullException(nameof(jwtTokenGenerator));
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var username = !string.IsNullOrWhiteSpace(request.Username)
            ? request.Username.Trim()
            : (!string.IsNullOrWhiteSpace(request.Phone) ? request.Phone.Trim() : request.Email?.Trim());

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username, phone, or email is required.");

        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new ArgumentException("Full name is required.");

        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new ArgumentException("Phone number is required.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            throw new ArgumentException("Password must be at least 6 characters.");

        // Check uniqueness against existing users
        if (await _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken))
            throw new ArgumentException($"Username '{username}' is already taken.");

        var cleanPhone = request.Phone.Trim();
        if (await _context.Users.AnyAsync(u => u.Phone == cleanPhone, cancellationToken))
            throw new ArgumentException($"Phone number '{cleanPhone}' is already registered.");

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var cleanEmail = request.Email.Trim().ToLower();
            if (await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == cleanEmail, cancellationToken))
                throw new ArgumentException($"Email '{request.Email}' is already registered.");
        }

        // Fetch or create default User role
        var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == RoleType.User, cancellationToken);
        if (defaultRole == null)
        {
            defaultRole = new Role { Name = RoleType.User, Description = "Default User" };
            _context.Roles.Add(defaultRole);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();
        var email = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email.Trim()
            : $"{username}@batterypartsstore.local";

        var user = new User
        {
            Username = username,
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = cleanPhone,
            PasswordHash = passwordHash,
            IsActive = true,
            RefreshToken = refreshToken,
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

        // Persist User and UserRole (PostgreSQL trigger trg_users_link_customer_on_register links customer atomically)
        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, new[] { RoleType.User });

        return new AuthResponseDto
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = 900,
            Roles = new List<string> { RoleType.User },
            User = new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                Phone = user.Phone,
                FullName = user.FullName,
                Roles = new List<string> { RoleType.User },
                CustomerId = user.Customer?.CustomerId
            }
        };
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var identifier = request.GetResolvedIdentifier();
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("Username, phone, or email and password are required.");

        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u =>
                u.Username.ToLower() == identifier.ToLower() ||
                (u.Email != null && u.Email.ToLower() == identifier.ToLower()) ||
                u.Phone == identifier, cancellationToken);

        if (user == null)
            throw new UnauthorizedAccessException("Invalid username or password.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("User account is inactive.");

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid username or password.");

        var roles = user.UserRoles
            .Select(ur => ur.Role.Name)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (roles.Count == 0)
            roles.Add(RoleType.User);

        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, roles);
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        // Support grace period / multi-client tokens: keep up to 3 recent tokens
        var tokensList = (user.RefreshToken ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        tokensList.Add(newRefreshToken);
        if (tokensList.Count > 3)
        {
            tokensList = tokensList.TakeLast(3).ToList();
        }

        user.RefreshToken = string.Join(";", tokensList);
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            TokenType = "Bearer",
            ExpiresIn = 900,
            Roles = roles,
            User = new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                Phone = user.Phone,
                FullName = user.FullName,
                Roles = roles,
                CustomerId = user.Customer?.CustomerId
            }
        };
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException("Refresh token is required.");

        var cleanToken = refreshToken.Trim();

        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.RefreshToken != null &&
                                      u.RefreshToken.Contains(cleanToken) &&
                                      u.RefreshTokenExpiryTime > DateTime.UtcNow, cancellationToken);

        if (user == null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("User account is inactive.");

        var validTokens = (user.RefreshToken ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!validTokens.Contains(cleanToken))
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var roles = user.UserRoles
            .Select(ur => ur.Role.Name)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        if (roles.Count == 0)
            roles.Add(RoleType.User);

        var newAccessToken = _jwtTokenGenerator.GenerateAccessToken(user, roles);
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        // Token rotation: append new token and keep up to 3 recent tokens
        var tokensList = validTokens.ToList();
        tokensList.Add(newRefreshToken);
        if (tokensList.Count > 3)
        {
            tokensList = tokensList.TakeLast(3).ToList();
        }

        user.RefreshToken = string.Join(";", tokensList);
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            TokenType = "Bearer",
            ExpiresIn = 900,
            Roles = roles,
            User = new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                Phone = user.Phone,
                FullName = user.FullName,
                Roles = roles,
                CustomerId = user.Customer?.CustomerId
            }
        };
    }

    public async Task<UserProfileDto> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user == null)
            throw new EntityNotFoundException(nameof(User), userId);

        var roles = user.UserRoles
            .Select(ur => ur.Role.Name)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        return new UserProfileDto
        {
            UserId = user.UserId,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AvatarUrl = user.AvatarUrl,
            IsActive = user.IsActive,
            CustomerId = user.Customer?.CustomerId,
            Roles = roles,
            CreatedAt = user.CreatedAt
        };
    }
}
