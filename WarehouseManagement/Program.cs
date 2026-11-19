using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using WarehouseManagement.BackgroundServices;
using WarehouseManagement.Services;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration;
var isDevelopment = builder.Environment.IsDevelopment();
var isTesting = builder.Environment.IsEnvironment("Testing");

builder.Host.UseSerilog((_, log) => log
    .WriteTo.Console()
    .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day));

var databaseProvider = settings["Database:Provider"] ?? "SqlServer";
var useSqlite = databaseProvider == "Sqlite";
if (useSqlite && !isDevelopment && !isTesting)
{
    throw new InvalidOperationException("The portable SQLite mode is intended for development and tests.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    switch (databaseProvider)
    {
        case "Sqlite":
            options.UseSqlite(settings.GetConnectionString("Sqlite") ?? "Data Source=warehouse-dev.db");
            break;
        case "SqlServer":
            options.UseSqlServer(settings.GetConnectionString("DefaultConnection"));
            break;
        default:
            throw new InvalidOperationException("Unknown database provider.");
    }
});

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IWarehouseEventService, WarehouseEventService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<EventGenerator>();
builder.Services.AddHostedService<EventGeneratorService>();
builder.Services.AddSingleton<EventCacheVersion>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 512);

var jwt = settings.GetSection("JwtSettings");
if (string.IsNullOrEmpty(jwt["SecretKey"]) && isDevelopment)
{
    jwt["SecretKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

var signingKey = Encoding.UTF8.GetBytes(jwt["SecretKey"] ?? "");
if (signingKey.Length < 32)
{
    throw new InvalidOperationException("Set JwtSettings__SecretKey to a random secret of at least 32 bytes.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            ClockSkew = TimeSpan.FromSeconds(15),
            IssuerSigningKey = new SymmetricSecurityKey(signingKey)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context =>
    {
        var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "WarehouseManagement", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter the token returned by /api/auth/login."
    });

    var bearer = new OpenApiSecurityScheme
    {
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = Array.Empty<string>() });
});

var app = builder.Build();
app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception error) when (error is ArgumentException or ValidationException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = error.Message });
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
    catch (Exception error)
    {
        var traceId = context.TraceIdentifier;
        app.Logger.LogError(error, "Request failed: {TraceId}", traceId);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error", traceId });
    }
});

if (isDevelopment)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!isDevelopment && !isTesting)
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (useSqlite)
    {
        await db.Database.EnsureCreatedAsync();
    }
    else if (settings.GetValue<bool>("Database:ApplyMigrations"))
    {
        await db.Database.MigrateAsync();
    }

    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    await DatabaseInitializer.SeedAsync(
        db, passwordHasher, settings["Bootstrap:AdminPassword"], settings["Bootstrap:ManagerPassword"]);
}

app.Run();

public partial class Program { }
