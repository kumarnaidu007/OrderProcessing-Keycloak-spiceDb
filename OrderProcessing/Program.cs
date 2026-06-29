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
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddDbContext<OrderProcessingContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var keycloak = builder.Configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>() ?? new KeycloakOptions();
var spiceDb = builder.Configuration.GetSection(SpiceDbOptions.SectionName).Get<SpiceDbOptions>() ?? new SpiceDbOptions();

var metadataBase = !string.IsNullOrWhiteSpace(keycloak.MetadataAuthority)
    ? keycloak.MetadataAuthority.TrimEnd('/')
    : keycloak.Authority.TrimEnd('/');
var keycloakConfigured = !string.IsNullOrWhiteSpace(keycloak.Audience) && !string.IsNullOrWhiteSpace(metadataBase);
if (!keycloakConfigured)
{
    throw new InvalidOperationException(
        "Keycloak is required: set Keycloak:Audience and Keycloak:Authority or Keycloak:MetadataAuthority (see appsettings and docker-compose).");
}

builder.Services.AddSingleton<IOptions<KeycloakOptions>>(Options.Create(keycloak));
builder.Services.AddSingleton<IOptions<SpiceDbOptions>>(Options.Create(spiceDb));
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IApplicationUserResolver, ApplicationUserResolver>();

builder.Services.AddHttpClient<ISpiceDbAuthorizationService, SpiceDbAuthorizationService>(client =>
{
    if (!string.IsNullOrWhiteSpace(spiceDb.HttpEndpoint))
        client.BaseAddress = new Uri(spiceDb.HttpEndpoint);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        options.MetadataAddress = $"{metadataBase}/.well-known/openid-configuration";
        options.Authority = metadataBase;
        var issuers = keycloak.ValidIssuers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.TrimEnd('/')).ToArray();
        if (issuers.Length == 0)
            issuers = new[] { metadataBase };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuers = issuers,
            ValidAudience = keycloak.Audience,
            NameClaimType = "sub",
            RoleClaimType = "roles"
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not System.Security.Claims.ClaimsIdentity identity)
                    return Task.CompletedTask;

                var realmAccessJson = identity.FindFirst("realm_access")?.Value;
                if (string.IsNullOrWhiteSpace(realmAccessJson))
                    return Task.CompletedTask;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(realmAccessJson);
                    if (doc.RootElement.TryGetProperty("roles", out var rolesNode) &&
                        rolesNode.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var role in rolesNode.EnumerateArray())
                        {
                            var roleValue = role.GetString();
                            if (!string.IsNullOrWhiteSpace(roleValue))
                                identity.AddClaim(new System.Security.Claims.Claim("roles", roleValue));
                        }
                    }
                }
                catch
                {
                    // Ignore malformed optional role payloads.
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyNames.ProductsManage, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.ProductsManage) ||
            ctx.User.IsInRole(Roles.Admin)));

    options.AddPolicy(PolicyNames.OrdersCreate, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersCreate) ||
            ctx.User.IsInRole(Roles.Admin) ||
            ctx.User.IsInRole(Roles.Customer)));

    options.AddPolicy(PolicyNames.AddressesManage, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.AddressesManage) ||
            ctx.User.IsInRole(Roles.Customer) ||
            ctx.User.IsInRole(Roles.Admin)));

    options.AddPolicy(PolicyNames.OrdersList, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadAll) ||
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadOwn) ||
            ctx.User.IsInRole(Roles.Admin) ||
            ctx.User.IsInRole(Roles.Customer)));

    options.AddPolicy(PolicyNames.OrdersRead, policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadAll) ||
            ctx.User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadOwn) ||
            ctx.User.IsInRole(Roles.Admin) ||
            ctx.User.IsInRole(Roles.Customer)));
});

// Register CountriesStore singleton as required by SCRUM-6 (do not register more than once)
builder.Services.AddSingleton<ICountriesStore, CountriesStore>();

// Configure System.Text.Json to preserve DTO property naming (do not apply camelCase globally)
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Preserve property names as declared or as specified by JsonPropertyName attributes
        opts.JsonSerializerOptions.PropertyNamingPolicy = null;
        opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Order Processing API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
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

var app = builder.Build();

// Enable middleware to serve swagger UI in non-production or production depending on ops
app.UseSwagger();
app.UseSwaggerUI();

// Ensure correlation id middleware is configured BEFORE exception handling and routing
app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-Id";
    var logger = app.Logger;

    string? correlationId = null;
    if (context.Request.Headers.TryGetValue(headerName, out var incoming) && !string.IsNullOrWhiteSpace(incoming))
    {
        correlationId = incoming.ToString();
    }

    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
        logger.LogDebug("Generated new correlation id {CorrelationId} for request {Path}", correlationId, context.Request.Path);
    }
    else
    {
        logger.LogDebug("Using provided correlation id {CorrelationId} for request {Path}", correlationId, context.Request.Path);
    }

    // Make correlation id available to other middleware/handlers and to logs
    context.TraceIdentifier = correlationId;
    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers[headerName] = correlationId;

    await next();
});

// Global exception handler middleware should be registered before routing so it catches exceptions early
app.UseExceptionHandling();

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
