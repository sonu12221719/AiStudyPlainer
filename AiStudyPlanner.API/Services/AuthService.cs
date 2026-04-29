using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using AiStudyPlanner.API.Data;
using AiStudyPlanner.API.Utilities;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using AiStudyPlanner.API.Models.Entities;

namespace AiStudyPlanner.API.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db     = db;
        _config = config;
    }

    // ── Register ───────────────────────────────────────────────────
    public async Task<RegisterResponseDto> RegisterAsync(RegisterDto dto)
    {
        var exists = await _db.Users
            .AnyAsync(u => u.Email == dto.Email.ToLower());

        if (exists)
            throw new InvalidOperationException("Email already registered.");

        var user = new User
        {
            Name             = dto.FullName.Trim(),
            Email            = dto.Email.ToLower().Trim(),
            Password         = PasswordHelper.Hash(dto.Password),
            ExamTarget       = dto.ExamTarget,
            ExamDate         = dto.ExamDate,
            DailyStudyHours  = dto.DailyStudyHours,
            CreatedAt        = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new RegisterResponseDto
        {
            UserId  = user.Id,
            Email   = user.Email,
            Message = "Registration successful."
        };
    }

    // ── Login ──────────────────────────────────────────────────────
    public async Task<LoginResponseDto> LoginAsync(LoginDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower());

        if (user is null || !PasswordHelper.Verify(dto.Password, user.Password))
            throw new UnauthorizedAccessException("Invalid email or password.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is deactivated.");

        var token        = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new LoginResponseDto
        {
            Token        = token,
            RefreshToken = refreshToken,
            ExpiresAt    = DateTime.UtcNow.AddMinutes(
                int.Parse(_config["Jwt:ExpiryMinutes"]!)),
            User = MapToProfileDto(user)
        };
    }

    // ── Refresh token ──────────────────────────────────────────────
    public async Task<LoginResponseDto> RefreshTokenAsync(string refreshToken)
    {
        // For a mini project, validate the token format and
        // issue a new JWT. In production, store refresh tokens in DB.
        throw new NotImplementedException(
            "Store refresh tokens in DB for production.");
    }

    // ── Get profile ────────────────────────────────────────────────
    public async Task<UserProfileDto> GetProfileAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        return MapToProfileDto(user);
    }

    // ── Update profile ─────────────────────────────────────────────
    public async Task<UserProfileDto> UpdateProfileAsync(
        int userId, UpdateProfileDto dto)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        user.Name            = dto.Name ?? user.Name;
        user.ExamTarget      = dto.ExamTarget ?? user.ExamTarget;
        user.ExamDate        = dto.ExamDate ?? user.ExamDate;
        user.DailyStudyHours = dto.DailyStudyHours != 0 ? dto.DailyStudyHours : user.DailyStudyHours;
        user.UpdatedAt       = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToProfileDto(user);
    }

    // ── Change password ────────────────────────────────────────────
    public async Task<bool> ChangePasswordAsync(
        int userId, string oldPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!PasswordHelper.Verify(oldPassword, user.Password))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        user.Password = PasswordHelper.Hash(newPassword);
        user.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Revoke token ───────────────────────────────────────────────
    public Task<bool> RevokeTokenAsync(string refreshToken)
    {
        // In production: delete refresh token from DB
        return Task.FromResult(true);
    }

    // ── Private helpers ────────────────────────────────────────────
    private string GenerateJwtToken(User user)
    {
        var key   = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Name,           user.Name),
            new Claim("examTarget",              user.ExamTarget ?? "")
        };

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(
                int.Parse(_config["Jwt:ExpiryMinutes"]!)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static UserProfileDto MapToProfileDto(User user) => new()
    {
        Id                = user.Id,
        FullName          = user.Name,
        Email             = user.Email,
        ExamTarget        = user.ExamTarget,
        ExamDate          = user.ExamDate,
        DailyStudyHours   = user.DailyStudyHours,
        ProfilePictureUrl = user.ProfilePictureUrl
    };
}