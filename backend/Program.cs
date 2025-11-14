using RHCSAExam.Services;

var builder = WebApplication.CreateBuilder(args);

// Error logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add services to the container
builder.Services.AddControllers();

// Register ProxmoxService with DI
builder.Services.AddSingleton<ProxmoxService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new ProxmoxService(config);
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// CRITICAL: Routing must come BEFORE CORS
app.UseRouting();
app.UseCors("AllowAllOrigins");

// Authorization middleware
app.UseAuthorization();

// Map controllers
app.MapControllers();

app.Run();
