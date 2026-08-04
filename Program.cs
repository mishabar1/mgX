using MG.Server.BL;
using MG.Server.Database;
using MG.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Persistence (C4): EF Core + SQLite. Registered as a factory so the singleton
// DataRepository can create short-lived contexts safely.
// NOTE for DigitalOcean: App Platform has an EPHEMERAL filesystem, so the SQLite
// file is wiped on every deploy/restart. Attach a persistent volume (Droplet) or
// switch the provider to Managed Postgres for durable production data.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=Database/app.db";
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

// ---------------------------------------------------------------------------
// Authentication (C2): self-issued JWT bearer.
// The signing key comes from configuration. In Development a throwaway key is
// used; in Production the app refuses to start without Jwt__Key (env var).
// ---------------------------------------------------------------------------
var jwtSettings = new JwtSettings();
builder.Configuration.GetSection("Jwt").Bind(jwtSettings);
if (string.IsNullOrWhiteSpace(jwtSettings.Key))
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSettings.Key = "dev-only-insecure-signing-key-change-me-0123456789ABCDEF";
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set the 'Jwt__Key' environment variable in production.");
    }
}
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR sends the token via the access_token query string (browsers can't set
        // Authorization headers on WebSocket handshakes). Read it for the hub path only.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/notifications"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<DataRepository, DataRepository>();
builder.Services.AddScoped<GameBL, GameBL>();
builder.Services.AddScoped<UserBL, UserBL>();

// ---------------------------------------------------------------------------
// CORS (C3): only the configured front-end origins are allowed. Combining
// AllowCredentials with a specific origin list is required for the SignalR hub.
// ---------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "MGX",
        policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.MaxDepth = 10;
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

var app = builder.Build();

// Ensure the SQLite schema exists, then load state and rebuild runtime game objects.
_ = app.Services.GetRequiredService<DataRepository>();

// Configure the HTTP request pipeline.
// (H5) Swagger only in Development — it was previously exposed in every environment.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// (H5) HTTPS redirect only outside Production. In the container, TLS is terminated
// by DigitalOcean, so redirecting here causes redirect loops.
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "wwwroot")),
    RequestPath = "",
    ServeUnknownFileTypes = true
});

app.UseRouting();

app.UseCors("MGX");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notifications");

// Replaces the removed SpaServices UseSpa(): client-side routing fallback.
app.MapFallbackToFile("index.html");

app.Run();
