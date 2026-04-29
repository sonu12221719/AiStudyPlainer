using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class RegisterResponseDto
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = "Registration successful.";
}
