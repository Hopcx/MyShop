using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyShop.Api.Common;
using MyShop.Api.Domain;
using MyShop.Api.Infrastructure;

namespace MyShop.Api.Modules.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "myshop";
    public string Audience { get; set; } = "myshop-clients";
    /// <summary>Khóa ký HS256 — bắt buộc ≥ 32 byte, đọc từ secret store (K8s Secret / Key Vault) ở production.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(200)] string FullName);

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record RefreshRequest([Required] string RefreshToken);
public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType, UserDto User);
public record UserDto(Guid Id, string Email, string FullName, string Role, string PreferredLocale);

public class TokenService(IOptions<JwtOptions> options, ShopDbContext db, TimeProvider clock)
{
    private readonly JwtOptions _opt = options.Value;

    public static SymmetricSecurityKey Key(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public async Task<TokenResponse> IssueAsync(User user, Guid? familyId = null, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var jwt = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims, now, now.AddMinutes(_opt.AccessTokenMinutes),
            new SigningCredentials(Key(_opt.SigningKey), SecurityAlgorithms.HmacSha256));

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refresh),
            FamilyId = familyId ?? Guid.NewGuid(),
            ExpiresAt = now.AddDays(_opt.RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);

        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(jwt), refresh, _opt.AccessTokenMinutes * 60,
            "Bearer", ToDto(user));
    }

    /// <summary>Refresh token rotation + reuse detection: dùng lại token đã bị thu hồi → thu hồi cả family.</summary>
    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                    ?? throw new DomainException("Refresh token không hợp lệ.", 401, "invalid_token");

        if (token.RevokedAt is not null)
        {
            await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            throw new DomainException("Refresh token đã bị sử dụng lại.", 401, "token_reused");
        }
        if (token.ExpiresAt <= now) throw new DomainException("Refresh token đã hết hạn.", 401, "token_expired");

        token.RevokedAt = now;
        var user = await db.Users.FindAsync([token.UserId], ct) ?? throw new DomainException("Người dùng không tồn tại.", 401);
        return await IssueAsync(user, token.FamilyId, ct);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static UserDto ToDto(User u) => new(u.Id, u.Email, u.FullName, u.Role, u.PreferredLocale);
}

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public class AuthController(ShopDbContext db, TokenService tokens, IPasswordHasher<User> hasher, ILogger<AuthController> log)
    : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw DomainException.Conflict("Email đã được sử dụng.", "email_taken");

        var user = new User { Email = email, FullName = req.FullName.Trim() };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        db.Users.Add(user);
        db.Enqueue("UserRegistered", new { user.Id, user.Email, user.FullName });
        await db.SaveChangesAsync(ct);
        log.LogInformation("User registered {Email}", LogMasking.MaskEmail(email));
        return await tokens.IssueAsync(user, ct: ct);
    }

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // Thông báo lỗi chung để tránh dò tài khoản.
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
        {
            log.LogWarning("Failed login for {Email}", LogMasking.MaskEmail(email));
            throw new DomainException("Email hoặc mật khẩu không đúng.", 401, "invalid_credentials");
        }
        return await tokens.IssueAsync(user, ct: ct);
    }

    [HttpPost("refresh")]
    public Task<TokenResponse> Refresh(RefreshRequest req, CancellationToken ct) => tokens.RefreshAsync(req.RefreshToken, ct);

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req, CancellationToken ct)
    {
        await tokens.RevokeAsync(req.RefreshToken, ct);
        return NoContent();
    }
}
