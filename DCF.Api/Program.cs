using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DcfDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
        opt.Audience = builder.Configuration["Auth0:Audience"];
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddScoped<ICorpsService, CorpsService>();
builder.Services.AddHttpClient<IRecapScraperTask, RecapScraperTask>();

builder.Services.AddSingleton<IMqttPublisherService, MqttPublisherService>();
builder.Services.AddHostedService(sp => (MqttPublisherService)sp.GetRequiredService<IMqttPublisherService>());

builder.Services.AddSingleton<ScrapeSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScrapeSchedulerService>());

builder.Services.AddSingleton<DraftSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DraftSchedulerService>());

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
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
