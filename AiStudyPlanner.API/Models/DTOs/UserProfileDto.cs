using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class UserProfileDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ExamTarget { get; set; }
    public DateTime? ExamDate { get; set; }
    public int DailyStudyHours { get; set; }
    public string? ProfilePictureUrl { get; set; }
}
