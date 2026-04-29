using Microsoft.EntityFrameworkCore;
using AiStudyPlanner.API.Models.Entities;

namespace AiStudyPlanner.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(){}
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }
        
    public virtual DbSet<User>          Users          { get; set; }
    public virtual DbSet<StudyPlan>     StudyPlans     { get; set; }
    public virtual DbSet<Topic>         Topics         { get; set; }
    public virtual DbSet<QuizResult>    QuizResults    { get; set; }
    public virtual DbSet<WeakArea>      WeakAreas      { get; set; }
    public virtual DbSet<SyllabusChunk> SyllabusChunks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Fluent API configurations can be added here if needed
        modelBuilder.Entity<SyllabusChunk>()
            .HasOne(sc => sc.User)
            .WithMany(u => u.SyllabusChunks)
            .HasForeignKey(sc => sc.UserId)
            .OnDelete(DeleteBehavior.Restrict); // or NoAction
        
        modelBuilder.Entity<QuizResult>()
            .HasOne(qr => qr.User)
            .WithMany(u => u.QuizResults)
            .HasForeignKey(qr => qr.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<WeakArea>()
            .HasOne(wa => wa.User)
            .WithMany(u => u.WeakAreas)
            .HasForeignKey(wa => wa.UserId)
            .OnDelete(DeleteBehavior.Restrict); // or DeleteBehavior.NoAction
    }
}