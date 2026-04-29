using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class SyllabusUploadDto
{
    public required IFormFile File { get; set; } = null!;
    public string? PlanTitle { get; set; }
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
    public int DailyStudyHours { get; set; } = 4;
}
