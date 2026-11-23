using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Identity.Entities;
using SupplyGuard.Infrastructure.Identity.Jwt;
using SupplyGuard.Infrastructure.Identity.Seeding;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Seeding;
using SupplyGuard.WebAPI.Authentication;
using SupplyGuard.WebAPI.Authentication.Authorization;
using SupplyGuard.WebAPI.RateLimiting;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<InitialAdministratorOptions>(
    builder.Configuration.GetSection(InitialAdministratorOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddJitRiskAnalysis();
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Configuration.GetValue<bool?>("JitRiskAnalysis:UseSimulatedIntegrations") is not null)
{
    // Fail fast instead of silently ignoring the replaced setting.
    throw new InvalidOperationException(
        "JitRiskAnalysis:UseSimulatedIntegrations was replaced by DemoMode:Enabled. Remove the old setting.");
}
// Explicit opt-in for a public demo, allowed in any environment; see DemoModeOptions.
builder.Services.AddDemoMode(builder.Configuration);
builder.Services.AddRabbitMqMessaging(builder.Configuration,
    builder.Configuration.GetValue("Messaging:RunBackgroundServices", true));
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection configuration was not found.");

    if (IsPublicPlaceholder(options.Configuration))
    {
        throw new InvalidOperationException(
            "Redis connection configuration contains a public placeholder. " +
            "Set ConnectionStrings:Redis with User Secrets or the " +
            "ConnectionStrings__Redis environment variable.");
    }
});

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<SupplyGuardDbContext>()
    .AddDefaultTokenProviders();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration was not found.");
var signingKey = jwtOptions.SigningKey
    ?? throw new InvalidOperationException("JWT signing key configuration was not found.");

if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
    string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
    IsPublicPlaceholder(signingKey) ||
    Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException(
        "JWT configuration is invalid. Set Jwt:SigningKey to a non-placeholder value of at least 32 UTF-8 " +
        "bytes with User Secrets or the Jwt__SigningKey environment variable.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
builder.Services.AddSupplyGuardRateLimiting(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSupplyGuardForwardedHeaders();
app.UseHttpsRedirection();

// Before authentication, so anonymous floods are rejected without token validation work.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var identitySeeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
    await identitySeeder.SeedAsync();

    // Both seeders are registered only when DemoMode:Enabled is true.
    var demoSuppliers = scope.ServiceProvider.GetService<DemoSupplierSeeder>();
    var demoUser = scope.ServiceProvider.GetService<DemoUserSeeder>();
    if (demoSuppliers is not null && demoUser is not null)
    {
        await demoSuppliers.SeedAsync();
        await demoUser.SeedAsync();
        app.Logger.LogWarning(
            "DemoMode is enabled: simulated supplier data, feature overrides and anonymous demo sign-in are active.");
    }
}

app.Run();

static bool IsPublicPlaceholder(string value) =>
    value.Contains("<YOUR_", StringComparison.OrdinalIgnoreCase);
