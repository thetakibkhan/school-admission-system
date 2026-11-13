using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// R10: an invalid HOLD_DURATION_SECONDS aborts startup with a logged error and a non-zero exit.
HoldOptions holdOptions;
try
{
    holdOptions = HoldOptions.FromConfiguration(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"FATAL: {ex.Message}");
    throw;
}

builder.Services.AddSingleton(holdOptions);

builder.Services.AddDbContext<AdmissionDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=school_admission";
    options.UseNpgsql(connectionString);
});

builder.Services.AddControllers();
builder.Services.AddScoped<SchoolAdmission.Api.Services.ClassService>();
builder.Services.AddScoped<SchoolAdmission.Api.Services.HoldService>();
builder.Services.AddProblemDetails();

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}

var app = builder.Build();

var frontendOrigin = app.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173";
app.UseCors(cors => cors
    .WithOrigins(frontendOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod());

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        var status = exception is DbUpdateException or Npgsql.NpgsqlException or TimeoutException
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status500InternalServerError;

        context.Response.StatusCode = status;

        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = "about:blank",
                Title = status == StatusCodes.Status503ServiceUnavailable
                    ? "Service unavailable"
                    : "Unexpected server error",
                Status = status,
                Detail = "A temporary problem occurred. Please retry."
            }
        });
    });
});

app.MapControllers();

var applyMigrations = string.Equals(
    app.Configuration["APPLY_MIGRATIONS"],
    "true",
    StringComparison.OrdinalIgnoreCase);

if (applyMigrations)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Applied {Count} pending migration(s).", pending.Count);
}

app.Run();

public partial class Program;
