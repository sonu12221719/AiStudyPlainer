using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class ExamPresetRequestDto
{
    [MaxLength(100)]
    public required string ExamName { get; set; } = string.Empty;
    // e.g. "JEE", "NEET", "UPSC", "GATE"

    public required DateTime StartDate { get; set; }

    public required DateTime EndDate { get; set; }

    public int DailyStudyHours { get; set; } = 4;

    public List<string> FocusSubjects { get; set; } = new();
    // optional: student picks which subjects to prioritise
}
