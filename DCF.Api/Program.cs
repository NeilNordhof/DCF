using DCF.Api;
using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DcfDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.WebHost.UseSentry(o =>
{
    o.Dsn = builder.Configuration["Sentry:Dsn"];
    o.Debug = builder.Environment.IsDevelopment();
    o.TracesSampleRate = builder.Environment.IsDevelopment() ? 1.0 : 0.1;
    o.EnableLogs = true;
    o.SendDefaultPii = true;

    o.SetBeforeSend((sentryEvent, _) =>
    {
        var request = sentryEvent.Request;

        if (request is not null)
        {
            request.Cookies = null;

            if (request.Headers is not null)
            {
                foreach (var key in request.Headers.Keys.Where(k => string.Equals(k, "Cookie", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    request.Headers.Remove(key);
                }
            }
        }

        return sentryEvent;
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISentryUserFactory, Auth0SentryUserFactory>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(JwtBearerDefaults.AuthenticationScheme, null);
}
else
{
    const string Auth0Scheme = "Auth0Jwt";
    const string RememberMeScheme = "RememberMe";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddPolicyScheme(JwtBearerDefaults.AuthenticationScheme, "Auth0 or RememberMe", opt =>
        {
            opt.ForwardDefaultSelector = context =>
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault() ?? string.Empty;
                var token = authHeader.StartsWith("Bearer ") ? authHeader["Bearer ".Length..].Trim() : string.Empty;

                return token.Count(c => c == '.') == 2 ? Auth0Scheme : RememberMeScheme;
            };
        })
        .AddJwtBearer(Auth0Scheme, opt =>
        {
            opt.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
            opt.Audience = builder.Configuration["Auth0:Audience"];
        })
        .AddScheme<AuthenticationSchemeOptions, RememberMeAuthHandler>(RememberMeScheme, null);
}

builder.Services.AddAuthorization();
builder.Services.AddControllers(options => options.Filters.Add<SentryLeagueTaggingFilter>())
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddScoped<ICorpsService, CorpsService>();
builder.Services.AddHttpClient<IHtmlFetcher, HttpHtmlFetcher>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
});
builder.Services.AddTransient<IRecapScraperTask, RecapScraperTask>();
builder.Services.AddTransient<IShowInfoScraperTask, ShowInfoScraperTask>();

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

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.AddTransient<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<EmailTokenService>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddScoped<IStandingsService, StandingsService>();
builder.Services.AddScoped<IDraftService, DraftService>();
builder.Services.AddScoped<IRememberMeTokenService, RememberMeTokenService>();
builder.Services.AddScoped<IDciPublicService, DciPublicService>();

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
{
    if (builder.Environment.IsDevelopment())
    {
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    }
    else
    {
        p.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:5173")
         .AllowAnyMethod()
         .AllowAnyHeader();
    }
}));

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddHttpClient();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

    await db.Database.MigrateAsync();
}

var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(Path.Combine(uploadsPath, "corps-icons"));

app.UseCors();
app.UseExceptionHandler();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
