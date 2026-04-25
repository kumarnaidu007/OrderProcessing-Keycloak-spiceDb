using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OrderProcessing.Common;
using OrderProcessing.Models;
using OrderProcessing.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderProcessingContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    jwt.SigningKey = "DEV_ONLY_SIGNING_KEY_MIN_32_CHARS!!";
if (string.IsNullOrWhiteSpace(jwt.Issuer))
    jwt.Issuer = "OrderProcessing";
if (string.IsNullOrWhiteSpace(jwt.Audience))
    jwt.Audience = "OrderProcessing";

builder.Services.AddSingleton<IOptions<JwtOptions>>(Options.Create(jwt));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var code in PermissionCodes.All)
    {
        options.AddPolicy("Perm:" + code, policy =>
            policy.RequireClaim(AppClaims.Permission, code));
    }

    options.AddPolicy(PolicyNames.OrdersList, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadAll) ||
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadOwn)));

    options.AddPolicy(PolicyNames.OrdersRead, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadAll) ||
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadOwn)));
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Order Processing API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste access token from POST /api/auth/verify-otp (after OTP) or POST /api/auth/refresh"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHostedService<OrderProcessingWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderProcessingContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    await db.Database.EnsureCreatedAsync();
    await DataSeeder.SeedAsync(db, config, loggerFactory);
}

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

public partial class Program { }
