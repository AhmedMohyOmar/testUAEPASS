using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Core Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "UAE PASS Automation API", Version = "v1" });
});

// 2. Custom Store Registration
builder.Services.AddSingleton<AuthCodeStore>();

var app = builder.Build();

// 3. Middleware Pipeline (Order Matters!)
// Enabled for ALL environments so it works in Azure
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "UAE PASS API V1");
});

app.UseAuthorization();
app.MapControllers();

app.Run();

// Keep your AuthCodeStore class as is below...

public sealed class AuthCodeStore
{
    //test
    private readonly object _lock = new();
    private readonly Dictionary<string, (string Code, DateTimeOffset CreatedAt)> _codes = new();

    public void Save(string state, string code)
    {
        lock (_lock)
        {
            _codes[state] = (code, DateTimeOffset.UtcNow);
        }
    }

    public bool TryGet(string state, out string? code)
    {
        lock (_lock)
        {
            if (_codes.TryGetValue(state, out var v))
            {
                code = v.Code;
                return true;
            }
        }
        code = null;
        return false;
    }
}
