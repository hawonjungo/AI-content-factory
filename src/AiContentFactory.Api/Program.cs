using AiContentFactory.Api.Middleware;
using AiContentFactory.Application;
using AiContentFactory.Infrastructure;
using AiContentFactory.Infrastructure.Persistence;
using Hangfire;
using HealthChecks.NpgSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---- Services ----
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration value.");

    builder.Services.AddHealthChecks()
        .AddNpgSql(postgresConnectionString, name: "postgres");

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host is "localhost" or "127.0.0.1"));
        }
        else
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();
            policy.WithOrigins(origins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

    var app = builder.Build();

    // Apply pending EF Core migrations on startup. Fine for MVP/local dev;
    // revisit (e.g. explicit migration step in CI/CD) before production.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseSerilogRequestLogging();
    app.UseMiddleware<CorrelationIdMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors("Frontend");
    app.UseHttpsRedirection();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");
    app.UseHangfireDashboard("/jobs");

    app.Run();
}
catch (HostAbortedException)
{
    // Thrown deliberately by EF Core design-time tooling (e.g. `dotnet ef migrations add`)
    // after it builds the service provider just to inspect the DbContext - not a real failure.
}
catch (Exception ex)
{
    Log.Fatal(ex, "AiContentFactory.Api terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
