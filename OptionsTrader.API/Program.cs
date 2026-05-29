using Microsoft.EntityFrameworkCore;
using OptionsTrader.Application.Interfaces;
using OptionsTrader.Application.Services;
using OptionsTrader.Infrastructure.Persistence;
using OptionsTrader.Infrastructure.Persistence.Repositories;
using OptionsTrader.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ITradeRepository, TradeRepository>();
builder.Services.AddScoped<IScreenshotRepository, ScreenshotRepository>();
builder.Services.AddScoped<IBrokerSettingRepository, BrokerSettingRepository>();
builder.Services.AddScoped<IScreenshotStorage, S3ScreenshotStorage>();
builder.Services.AddScoped<TradeService>();
builder.Services.AddScoped<ScreenshotService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
