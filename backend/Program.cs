var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// builder.Services.AddSingleton<ProxmoxService>();

var app = builder.Build();

// Middleware
app.UseHttpsRedirection();

app.UseRouting();

// Authorization (optional)
app.UseAuthorization();

// Map controllers
app.MapControllers();

app.Run();
