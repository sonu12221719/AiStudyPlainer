using System;

namespace AiStudyPlanner.API.Models.DTOs;

public class DailyScheduleDto
{
    public int DayNumber { get; set; }
    public DateTime Date { get; set; }
    public List<TopicSummaryDto> Topics { get; set; } = new();
    public int TotalMinutes { get; set; }
}
