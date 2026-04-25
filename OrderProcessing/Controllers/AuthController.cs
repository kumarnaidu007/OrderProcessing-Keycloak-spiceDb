using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderProcessing.Common;
using OrderProcessing.Dtos.Auth;
using OrderProcessing.Models;

namespace OrderProcessing.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string OtpPurposeLogin = "Login";

    private readonly OrderProcessingContext _db;
    private readonly JwtOptions _jwt;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        OrderProcessingContext db,
        IOptions<JwtOptions> jwtOptions,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwt = jwtOptions.Value;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginOtpChallengeResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Register requested for email {Email}.", request.Email);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await _db.Users.AnyAsync(u => u.Email == request.Email, ct))
            {
                _logger.LogWarning("Register conflict for email {Email}.", request.Email);
                return Conflict(new { message = "Email already registered." });
            }

            var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == Roles.Customer, ct);
            if (customerRole is null)
            {
                _logger.LogError("Register failed because customer role is not seeded.");
                return StatusCode(500, new { message = "Roles not seeded. Restart the application." });
            }

            var now = DateTime.UtcNow;
            var user = new User
            {
                Email = request.Email,
                DisplayName = request.DisplayName,
                Phone = "0000000000",
                IsActive = true,
                IsEmailVerified = false,
                IsPhoneVerified = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var customer = new Customer
            {
                User = user,
                DisplayName = request.DisplayName,
                Email = request.Email,
                Phone = "",
                ExternalReference = null,
                IsActive = true,
                CreatedAtUtc = now
            };
            _db.Customers.Add(customer);

            _db.UserRoles.Add(new UserRole
            {
                User = user,
                RoleId = customerRole.RoleId,
                AssignedAtUtc = now
            });

            AuditLogWriter.Add(_db, nameof(User), user.UserId.ToString(), "user.register", user.UserId,
                new { request.Email });
            await _db.SaveChangesAsync(ct);
            var challenge = await IssueLoginOtpAsync(user, request.Email, ct);
            await tx.CommitAsync(ct);

            challenge.Message = "Account created. Verify the OTP to sign in.";
            _logger.LogInformation("Register succeeded for user {UserId}.", user.UserId);
            return Ok(challenge);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Register failed for email {Email}.", request.Email);
            return StatusCode(500, new { message = "Unexpected error during registration." });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginOtpChallengeResponse>> Login([FromBody] RequestOtpRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Login requested for email {Email}.", request.Email);
        try
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);
            if (user is null)
            {
                _logger.LogWarning("Login failed. Unknown email {Email}.", request.Email);
                return NotFound(new { message = "No account with this email." });
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login blocked. Inactive user {UserId}.", user.UserId);
                return Unauthorized(new { message = "Account disabled." });
            }

            var challenge = await IssueLoginOtpAsync(user, request.Email, ct);
            challenge.Message =
                "An OTP was generated for this email. Call POST /api/auth/verify-otp with LoginOtpId and Code to receive your access token and refresh token.";
            _logger.LogInformation("Login OTP issued for user {UserId}.", user.UserId);
            return Ok(challenge);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for email {Email}.", request.Email);
            return StatusCode(500, new { message = "Unexpected error during login." });
        }
    }

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken ct)
    {
        _logger.LogInformation("OTP verification requested for LoginOtpId {LoginOtpId}.", request.LoginOtpId);
        var code = (request.Code ?? "").Trim();
        if (code.Length == 0)
            return BadRequest(new { message = "Code is required." });
        try
        {
            var otpRow = await _db.UserLoginOtps.FirstOrDefaultAsync(o => o.LoginOtpId == request.LoginOtpId, ct);
            if (otpRow is null)
                return NotFound(new { message = "Unknown or expired OTP request." });

        if (otpRow.Purpose != OtpPurposeLogin)
            return BadRequest(new { message = "Invalid OTP purpose." });

        if (otpRow.ConsumedAtUtc is not null)
            return BadRequest(new { message = "This OTP was already used. Request a new one via login." });

        if (otpRow.ExpiresAtUtc < DateTime.UtcNow)
            return BadRequest(new { message = "This OTP has expired. Request a new one via login." });

        if (otpRow.AttemptCount >= otpRow.MaxAttempts)
            return BadRequest(new { message = "Too many failed attempts. Request a new OTP via login." });

        var pepper = GetOtpPepper();
        var expectedHash = HashOtp(code, pepper);
        byte[] storedBytes;
        byte[] expectedBytes;
        try
        {
            storedBytes = Convert.FromBase64String(otpRow.OtpCodeHash);
            expectedBytes = Convert.FromBase64String(expectedHash);
        }
        catch (FormatException)
        {
            otpRow.AttemptCount += 1;
            await _db.SaveChangesAsync(ct);
            return Unauthorized(new { message = "Invalid code." });
        }

        var match = storedBytes.Length == expectedBytes.Length
                    && CryptographicOperations.FixedTimeEquals(storedBytes, expectedBytes);

        if (!match)
        {
            otpRow.AttemptCount += 1;
            await _db.SaveChangesAsync(ct);
            return Unauthorized(new
            {
                message = "Invalid code.",
                attemptsRemaining = Math.Max(0, otpRow.MaxAttempts - otpRow.AttemptCount)
            });
        }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == otpRow.UserId, ct);
            if (user is null || !user.IsActive)
                return Unauthorized(new { message = "Account not available." });

        var roleNames = await _db.UserRoles
            .Where(ur => ur.UserId == user.UserId)
            .Join(_db.Roles, ur => ur.RoleId, r => r.RoleId, (_, r) => r.RoleName)
            .ToListAsync(ct);

        if (roleNames.Count == 0)
            return Unauthorized(new { message = "No roles assigned." });

        otpRow.ConsumedAtUtc = DateTime.UtcNow;
        user.LastLoginAtUtc = DateTime.UtcNow;

        var permissionCodes = await LoadPermissionCodesAsync(user.UserId, ct);
        var refreshPlain = TokenCrypto.GenerateRefreshTokenPlainText();
        var refreshHash = TokenCrypto.HashRefreshToken(refreshPlain);
        var sessionId = Guid.NewGuid();
        var refreshDays = _configuration.GetValue("Auth:RefreshTokenDays", 14);
        var now = DateTime.UtcNow;
        var ua = Request.Headers.UserAgent.ToString();
        if (ua.Length > 512)
            ua = ua[..512];

        var session = new UserSession
        {
            SessionId = sessionId,
            UserId = user.UserId,
            RefreshTokenHash = refreshHash,
            DeviceName = "Web",
            UserAgent = ua,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(refreshDays),
            RevokedAtUtc = null,
            LastActivityAtUtc = now
        };
        _db.UserSessions.Add(session);

        AuditLogWriter.Add(_db, nameof(UserSession), sessionId.ToString(), "session.create", user.UserId,
            new { user.Email });
        await _db.SaveChangesAsync(ct);

            var token = BuildTokenResponse(user, roleNames, permissionCodes, sessionId);
            token.RefreshToken = refreshPlain;
            _logger.LogInformation("OTP verified successfully for user {UserId}.", user.UserId);
            return Ok(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTP verification failed for LoginOtpId {LoginOtpId}.", request.LoginOtpId);
            return StatusCode(500, new { message = "Unexpected error during OTP verification." });
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Refresh requested for session {SessionId}.", request.SessionId);
        try
        {
            var hash = TokenCrypto.HashRefreshToken(request.RefreshToken.Trim());
            var session = await _db.UserSessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(
                    s => s.SessionId == request.SessionId && s.RefreshTokenHash == hash && s.RevokedAtUtc == null &&
                         s.ExpiresAtUtc > DateTime.UtcNow,
                    ct);

            if (session?.User is null || !session.User.IsActive)
                return Unauthorized(new { message = "Invalid or expired session." });

        var roleNames = await _db.UserRoles
            .Where(ur => ur.UserId == session.UserId)
            .Join(_db.Roles, ur => ur.RoleId, r => r.RoleId, (_, r) => r.RoleName)
            .ToListAsync(ct);

        var permissionCodes = await LoadPermissionCodesAsync(session.UserId, ct);
        var refreshPlain = TokenCrypto.GenerateRefreshTokenPlainText();
        session.RefreshTokenHash = TokenCrypto.HashRefreshToken(refreshPlain);
        session.LastActivityAtUtc = DateTime.UtcNow;
        var refreshDays = _configuration.GetValue("Auth:RefreshTokenDays", 14);
        session.ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshDays);
        await _db.SaveChangesAsync(ct);

            var token = BuildTokenResponse(session.User, roleNames, permissionCodes, session.SessionId);
            token.RefreshToken = refreshPlain;
            _logger.LogInformation("Refresh succeeded for session {SessionId}.", request.SessionId);
            return Ok(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed for session {SessionId}.", request.SessionId);
            return StatusCode(500, new { message = "Unexpected error during refresh." });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        _logger.LogInformation("Logout requested.");
        try
        {
            var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var sid = User.FindFirstValue(AppClaims.SessionId);
            if (string.IsNullOrEmpty(sid) || !Guid.TryParse(sid, out var sessionId))
                return BadRequest(new { message = "Access token does not contain an active session id." });

            var session = await _db.UserSessions.FirstOrDefaultAsync(
                s => s.SessionId == sessionId && s.UserId == userId && s.RevokedAtUtc == null, ct);
            if (session is null)
                return Ok();

            session.RevokedAtUtc = DateTime.UtcNow;
            AuditLogWriter.Add(_db, nameof(UserSession), sessionId.ToString(), "session.revoke", userId, null);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Session {SessionId} revoked for user {UserId}.", sessionId, userId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout failed.");
            return StatusCode(500, new { message = "Unexpected error during logout." });
        }
    }

    private async Task<List<string>> LoadPermissionCodesAsync(long userId, CancellationToken ct) =>
        await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .SelectMany(ur => ur.Role.Permissions.Select(p => p.PermissionCode))
            .Distinct()
            .ToListAsync(ct);

    private TokenResponse BuildTokenResponse(User user, IList<string> roleNames, IList<string> permissionCodes, Guid sessionId)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_jwt.SigningKey);
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes <= 0 ? 60 : _jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AppClaims.SessionId, sessionId.ToString("D"))
        };
        foreach (var r in roleNames.Distinct())
            claims.Add(new Claim(ClaimTypes.Role, r));
        foreach (var p in permissionCodes.Distinct())
            claims.Add(new Claim(AppClaims.Permission, p));

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResponse
        {
            AccessToken = jwt,
            ExpiresInSeconds = (int)(expires - DateTime.UtcNow).TotalSeconds,
            SessionId = sessionId,
            RefreshToken = ""
        };
    }

    private async Task<LoginOtpChallengeResponse> IssueLoginOtpAsync(User user, string loginIdentifier, CancellationToken ct)
    {
        var plainCode = GenerateNumericOtp(6);
        var pepper = GetOtpPepper();
        var hash = HashOtp(plainCode, pepper);
        var lifetimeMinutes = _configuration.GetValue("Otp:LifetimeMinutes", 10);
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(lifetimeMinutes);

        var row = new UserLoginOtp
        {
            UserId = user.UserId,
            LoginIdentifier = loginIdentifier.Trim(),
            OtpCodeHash = hash,
            Purpose = OtpPurposeLogin,
            ExpiresAtUtc = expires,
            ConsumedAtUtc = null,
            AttemptCount = 0,
            MaxAttempts = 5,
            CreatedAtUtc = now
        };
        _db.UserLoginOtps.Add(row);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("OTP generated for user {UserId}, expires at {ExpiresAtUtc}.", user.UserId, expires);
        return new LoginOtpChallengeResponse
        {
            Message = "",
            LoginOtpId = row.LoginOtpId,
            ExpiresAtUtc = expires,
            LoginIdentifier = loginIdentifier.Trim(),
            DevOtpCode = _environment.IsDevelopment() ? plainCode : null
        };
    }

    private string GetOtpPepper() =>
        _configuration["Otp:Pepper"] ?? "order-processing-otp-pepper-change-in-production";

    private static string GenerateNumericOtp(int digits)
    {
        var min = (int)Math.Pow(10, digits - 1);
        var max = (int)Math.Pow(10, digits) - 1;
        return Random.Shared.Next(min, max + 1).ToString();
    }

    private static string HashOtp(string code, string pepper)
    {
        var payload = Encoding.UTF8.GetBytes(code + "\0" + pepper);
        var hash = SHA256.HashData(payload);
        return Convert.ToBase64String(hash);
    }
}
