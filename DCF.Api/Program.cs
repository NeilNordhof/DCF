using DCF.Api;
using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DcfDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(JwtBearerDefaults.AuthenticationScheme, null);
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opt =>
        {
            opt.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
            opt.Audience = builder.Configuration["Auth0:Audience"];
        });
}

builder.Services.AddAuthorization();
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddScoped<ICorpsService, CorpsService>();
builder.Services.AddHttpClient<IRecapScraperTask, RecapScraperTask>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
});

builder.Services.AddSingleton<IPresenceService, PresenceService>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService(sp => (MqttService)sp.GetRequiredService<IMqttService>());

builder.Services.AddSingleton<ScrapeSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScrapeSchedulerService>());

builder.Services.AddSingleton<DraftSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DraftSchedulerService>());

builder.Services.AddSingleton<SeasonStatusService>();
builder.Services.AddSingleton<ISeasonStatusService>(sp => sp.GetRequiredService<SeasonStatusService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SeasonStatusService>());

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddScoped<IStandingsService, StandingsService>();
builder.Services.AddScoped<IDraftService, DraftService>();

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:5173")
     .AllowAnyMethod()
     .AllowAnyHeader()));

builder.Services.AddHttpClient();

var app = builder.Build();

var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(Path.Combine(uploadsPath, "corps-icons"));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
