using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AiStudyPlanner.API.Interfaces;
using AiStudyPlanner.API.Models.DTOs;
using System.Security.Claims;

namespace AiStudyPlanner.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // ─── POST api/auth/register ────────────────────────────────────
    /// <summary>Register a new student account</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterResponseDto), 201)]
    [ProducesResponseType(typeof(ErrorResponseDto),    400)]
    [ProducesResponseType(typeof(ErrorResponseDto),    409)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        // Check password strength before hitting the service
        var strength = Utilities.PasswordHelper.CheckStrength(dto.Password);
        if (!strength.IsStrong)
            return BadRequest(new ErrorResponseDto
            {
                Message = "Password is too weak.",
                Errors  = strength.Errors
            });

        var result = await _authService.RegisterAsync(dto);
        return CreatedAtAction(
            nameof(GetProfile),
            new { },
            result);
    }

    // ─── POST api/auth/login ───────────────────────────────────────
    /// <summary>Login and receive JWT token</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    [ProducesResponseType(typeof(ErrorResponseDto), 401)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var result = await _authService.LoginAsync(dto);
        return Ok(result);
    }

    // ─── POST api/auth/refresh ─────────────────────────────────────
    /// <summary>Refresh an expired JWT using a refresh token</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 401)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequest(new ErrorResponseDto
            {
                Message = "Refresh token is required."
            });

        var result = await _authService.RefreshTokenAsync(dto.RefreshToken);
        return Ok(result);
    }

    // ─── POST api/auth/logout ──────────────────────────────────────
    /// <summary>Revoke the refresh token on logout</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenDto dto)
    {
        await _authService.RevokeTokenAsync(dto.RefreshToken);
        return Ok(new { message = "Logged out successfully." });
    }

    // ─── GET api/auth/profile ──────────────────────────────────────
    /// <summary>Get the logged-in student's profile</summary>
    [HttpGet("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 401)]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetUserId();
        var result = await _authService.GetProfileAsync(userId);
        return Ok(result);
    }

    // ─── PUT api/auth/profile ──────────────────────────────────────
    /// <summary>Update the logged-in student's profile</summary>
    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    [ProducesResponseType(typeof(ErrorResponseDto), 401)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var userId = GetUserId();
        var result = await _authService.UpdateProfileAsync(userId, dto);
        return Ok(result);
    }

    // ─── PUT api/auth/change-password ─────────────────────────────
    /// <summary>Change password for the logged-in student</summary>
    [HttpPut("change-password")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ErrorResponseDto), 400)]
    [ProducesResponseType(typeof(ErrorResponseDto), 401)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        if (dto.NewPassword != dto.ConfirmNewPassword)
            return BadRequest(new ErrorResponseDto
            {
                Message = "New passwords do not match."
            });

        var strength = Utilities.PasswordHelper.CheckStrength(dto.NewPassword);
        if (!strength.IsStrong)
            return BadRequest(new ErrorResponseDto
            {
                Message = "New password is too weak.",
                Errors  = strength.Errors
            });

        var userId = GetUserId();
        await _authService.ChangePasswordAsync(
            userId, dto.OldPassword, dto.NewPassword);

        return Ok(new { message = "Password changed successfully." });
    }

    // ─── GET api/auth/me ───────────────────────────────────────────
    /// <summary>Quick check — returns userId from JWT claims</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId    = GetUserId(),
            email     = User.FindFirstValue(ClaimTypes.Email),
            fullName  = User.FindFirstValue(ClaimTypes.Name),
            examTarget = User.FindFirstValue("examTarget")
        });
    }

    // ─── Private helpers ───────────────────────────────────────────
    private int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");

        return int.Parse(claim);
    }

    private ErrorResponseDto BuildValidationError()
    {
        var errors = ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return new ErrorResponseDto
        {
            Message = "Validation failed.",
            Errors  = errors
        };
    }
}

// ─── Supporting DTOs (auth-specific, only used by this controller) ──
public class RefreshTokenDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    [System.ComponentModel.DataAnnotations.Required]
    public string OldPassword { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class ErrorResponseDto
{
    public string       Message { get; set; } = string.Empty;
    public List<string> Errors  { get; set; } = new();
    public DateTime     Timestamp { get; set; } = DateTime.UtcNow;
}