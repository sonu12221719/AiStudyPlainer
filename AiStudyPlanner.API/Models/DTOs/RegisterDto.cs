using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class RegisterDto
{
    [MaxLength(100)]
    public required string FullName { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(255)]
    public required string Email { get; set; } = string.Empty;

    [MinLength(6)]
    [MaxLength(100)]
    public required string Password { get; set; } = string.Empty;

    // [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    // public required string ConfirmPassword { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ExamTarget { get; set; }

    public DateTime? ExamDate { get; set; }

    public int DailyStudyHours { get; set; } = 4;
}
