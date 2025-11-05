var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Placeholder root endpoint
app.MapGet("/", () => Results.Ok(new { name = "GameBot Service", status = "ok" }));

app.Run();
