// using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using AiStudyPlanner.API.Data;
using AiStudyPlanner.API.Interfaces;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using AiStudyPlanner.API.Services;
using Microsoft.OpenApi;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

// ─── 1. CONTROLLERS ───────────────────────────────────────────────
builder.Services.AddControllers();

// ─── 2. DATABASE (Entity Framework Core) ──────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register QdrantClient
builder.Services.AddSingleton<QdrantClient>(_ =>
    new QdrantClient("localhost", 6334));

// ─── 3. JWT AUTHENTICATION ─────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// ─── 4. CORS (allow Angular dev server) ───────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",   
                "https://yourdomain.com"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// // ─── 5. DEPENDENCY INJECTION — SERVICES ───────────────────────────
builder.Services.AddScoped<IAuthService,     AuthService>();
builder.Services.AddScoped<IGeminiService,   GeminiService>();
builder.Services.AddScoped<IPlannerService,  PlannerService>();
builder.Services.AddScoped<IProgressService, ProgressService>();
builder.Services.AddScoped<IVectorService, VectorService>();
builder.Services.AddScoped<IFileParserService, FileParserService>();

// ─── 6. HTTP CLIENT (for Gemini API calls) ─────────────────────────
builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout     = TimeSpan.FromSeconds(60);
});

// ─── 7. MEMORY CACHE (optional — cache Gemini responses) ──────────
// builder.Services.AddMemoryCache();

// ─── 8. SWAGGER ────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authentication using Bearer scheme"
    });
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc), new List<string>() }
    });
});

// ─── BUILD ─────────────────────────────────────────────────────────
var app = builder.Build();


// ─── 10. MIDDLEWARE PIPELINE ───────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseMiddleware<ExceptionMiddleware>(); // global error handler — must be first

app.UseHttpsRedirection();
 app.UseCors("AllowAngular");             // before auth
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();