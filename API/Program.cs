using Engine;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(builder.Configuration.GetSection("Redis:ConnectionString").Value?? "localhost:6379"));
builder.Services.AddSingleton<RateLimiterService>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    var logger = sp.GetRequiredService<ILogger<RateLimiterService>>();
    return new RateLimiterService(logger, redis, builder.Configuration.GetValue<int>("RateLimiter:MaxPerNumber"), builder.Configuration.GetValue<int>("RateLimiter:MaxPerAccount"));
});
//builder.Services.AddSingleton(sp =>
//    new RateLimiterService(sp.GetRequiredService<IConnectionMultiplexer>(),
//                            builder.Configuration.GetValue<int>("RateLimiter:MaxPerNumber"),
//                            builder.Configuration.GetValue<int>("RateLimiter:MaxPerAccount")));
//builder.Services.AddSingleton<RateLimiterService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
