using Amazon.S3;
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
var awsAccessKey = builder.Configuration["AWS:AccessKey"] ?? string.Empty;
var awsSecretKey = builder.Configuration["AWS:SecretKey"] ?? string.Empty;
var awsRegion    = builder.Configuration["AWS:Region"]    ?? "us-east-2";
var s3Bucket     = builder.Configuration["AWS:BucketName"] ?? "options-trader-screenshots";

var s3Client = new AmazonS3Client(
    awsAccessKey, awsSecretKey,
    Amazon.RegionEndpoint.GetBySystemName(awsRegion));

builder.Services.AddSingleton<IAmazonS3>(s3Client);
builder.Services.AddScoped<IScreenshotStorage>(sp =>
    new S3ScreenshotStorage(sp.GetRequiredService<IAmazonS3>(), s3Bucket));
builder.Services.AddScoped<TradeService>();
builder.Services.AddScoped<ScreenshotService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
