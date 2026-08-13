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

// Quiet the noisy EF Core SQL logging (every SELECT/UPDATE was printed at Info).
// Only surface EF warnings/errors; the rest of the app keeps its default levels.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

// ---------------------------------------------------------------------------
// Persistence (C4): EF Core + SQLite. Registered as a factory so the singleton
// DataRepository can create short-lived contexts safely.
// NOTE for DigitalOcean: App Platform has an EPHEMERAL filesystem, so the SQLite
// file is wiped on every deploy/restart. Attach a persistent volume (Droplet) or
// switch the provider to Managed Postgres for durable production data.
// ---------------------------------------------------------------------------
// The SQLite file must live in a WRITABLE location. In the container the app runs as a
// non-root user (can't write under /app) and the "Database/" folder isn't published — which
// caused "SQLite Error 14: unable to open database file" on startup. Use the OS temp dir,
// which is writable on both Linux containers and Windows dev. Data is ephemeral — fine for POC.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var dbPath = Path.Combine(Path.GetTempPath(), "mgx-app.db");
    connectionString = $"Data Source={dbPath}";
}
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
    // POC: auth is NOT enforced (controllers/hub have no [Authorize]) and no external key is
    // required, so the app always starts — no env var needed on DigitalOcean.
    // For real production: set Jwt__Key via environment and re-add [Authorize] (see git history).
    jwtSettings.Key = "poc-only-insecure-signing-key-change-me-0123456789ABCDEF";
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
    options.AddPolicy(name: "MGX", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Dev: allow any local origin so `ng serve` works on whatever port Vite picks.
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Server-driven UI panels (PlayerData.Screen) are deep UiNode trees (rows in cols in rows),
    // so 10 is too shallow. 64 is System.Text.Json's default ceiling and plenty.
    options.JsonSerializerOptions.MaxDepth = 64;
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

// PhysicalFileProvider throws if the folder is missing. In dev the built client (wwwroot) may not
// exist yet (you run `ng serve` separately) — create it so startup doesn't crash.
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var gameContentPath = Path.Combine(builder.Environment.ContentRootPath, "GameContent");
Directory.CreateDirectory(wwwrootPath);
Directory.CreateDirectory(gameContentPath);

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(wwwrootPath),
    RequestPath = "",
    ServeUnknownFileTypes = true
});

// Game CONTENT (art/models/sounds + fallback avatar heads) lives with the SERVER — the single
// source of truth. Served from GameContent/ at /games and /heads. Adding a new game's assets is a
// server-only change (drop files here); no client rebuild. A permissive CORS header lets the 3D
// loader/thumbnailer fetch them cross-origin from the dev client.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(gameContentPath),
    RequestPath = "",
    ServeUnknownFileTypes = true,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*"
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
