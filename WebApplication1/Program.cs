using Microsoft.EntityFrameworkCore;
using AMS.Models;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using AMS.Services;
using AMS.Data;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jwt:Key is missing. Set a strong secret in appsettings.json or environment variables.");
        }

        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
            ValidIssuer = jwtIssuer,

            ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
            ValidAudience = jwtAudience,

            ValidateLifetime = true,

            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Support JWT-in-HttpOnly-cookie so Razor page navigation works.
                var token = context.Request.Cookies["access_token"];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // For browser page loads, redirect to login; for non-GET (fetch/api), keep 401.
                var accept = context.Request.Headers.Accept.ToString();
                var isHtmlGet = HttpMethods.IsGet(context.Request.Method) && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
                var isApi = context.Request.Path.StartsWithSegments("/api");

                if (!isApi && isHtmlGet)
                {
                    context.HandleResponse();
                    context.Response.Redirect("/Auth/Login");
                }

                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                var accept = context.Request.Headers.Accept.ToString();
                var isHtmlGet = HttpMethods.IsGet(context.Request.Method) && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
                var isApi = context.Request.Path.StartsWithSegments("/api");

                if (!isApi && isHtmlGet)
                {
                    context.Response.Redirect("/Account/AccessDenied");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Make *every* endpoint require auth unless [AllowAnonymous] is used.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddScoped<IInstitutionService, InstitutionService>();
builder.Services.AddScoped<IAttendanceHybridService, AttendanceHybridService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (app.Environment.IsDevelopment())
{
    await DbSeeder.SeedAsync(app.Services);
}

app.Run();
