using RHCSAExam.Services;

var builder = WebApplication.CreateBuilder(args);


//error logging
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

var app = builder.Build();

app.UseRouting();

// Optional: authorization middleware
app.UseAuthorization();

// Map controllers
app.MapControllers();

app.Run();
