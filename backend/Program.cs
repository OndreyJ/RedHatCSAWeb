using RHCSAExam.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configuration is automatically loaded from:
// 1. appsettings.json
// 2. appsettings.{Environment}.json (e.g., appsettings.Development.json)
// 3. Environment variables

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}

// Add services to the container
builder.Services.AddControllers();

// Register ProxmoxService with appropriate lifetime
builder.Services.AddSingleton<ProxmoxService>();

// Configure CORS with credentials support (required for cookie-based auth)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Development: Allow localhost with credentials
            policy.WithOrigins(
                    "http://localhost:4200",
                    "http://localhost:5051",
                    "http://localhost:8080"
                  )
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials(); // Required for cookies
        }
        else
        {
            // Production: Restrict to specific origins with credentials
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials(); // Required for cookies
        }
    });
});

// Add health checks (optional but recommended)
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure middleware pipeline
// Order matters: Exception handling -> HTTPS -> Routing -> CORS -> Auth -> Endpoints

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Don't use HTTPS redirection in Docker/containerized environments
// Configure HTTPS at the reverse proxy/load balancer level instead

// CORS must come after UseRouting and before UseAuthorization
app.UseRouting();
app.UseCors("AllowAllOrigins");

app.UseAuthentication(); // Add if using authentication
app.UseAuthorization();

// Map endpoints
app.MapControllers();
app.MapHealthChecks("/health");

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("Application is shutting down...");
});

app.Run();
