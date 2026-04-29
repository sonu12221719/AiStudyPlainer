using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class UpdateProfileDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ExamTarget { get; set; }
    public DateTime? ExamDate { get; set; }
    public int DailyStudyHours { get; set; }
}
