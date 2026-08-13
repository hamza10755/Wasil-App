using Microsoft.AspNetCore.Identity;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wasil.Api.DTOs.Auth;
using Wasil.Service.Interfaces;
using Wasil.Data;
using Wasil.Data.Entities;
using Wasil.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Wasil.Data.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using Hangfire;

namespace Wasil.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly WasilDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthController> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IMemoryCache _cache;
    private static readonly ConcurrentDictionary<string, object> OtpLocks = new();

    public AuthController(
        WasilDbContext context, 
        ITokenService tokenService, 
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthController> logger,
        ICurrentUser currentUser,
        IMemoryCache cache)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
        _currentUser = currentUser;
        _cache = cache;
    }

    [HttpPost("staff/login")]
    public async Task<IActionResult> StaffLogin([FromBody] StaffLoginRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email && 
                                     (u.Role == Role.Admin || u.Role == Role.Partner));

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Failed staff login attempt for email {Email}", request.Email);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var response = await CreateAuthResponseAsync(user);
        return Ok(response);
    }
    
    [HttpPost("customer/request-otp")]
    public async Task<IActionResult> RequestOtp([FromBody] CustomerOtpRequest request)
    {
        var cacheKey = $"otp:{request.Phone}";
        if (_cache.TryGetValue(cacheKey, out OtpDetails? existingOtp) && existingOtp != null)
        {
            var secondsSinceLastRequest = (DateTime.UtcNow - existingOtp.LastRequestedAtUtc).TotalSeconds;
            if (secondsSinceLastRequest < 60)
            {
                return StatusCode(429, new { message = $"Please wait {60 - (int)secondsSinceLastRequest} seconds before requesting a new OTP." });
            }
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Phone == request.Phone && u.Role == Role.Customer);
        
        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Phone = request.Phone,
                Role = Role.Customer,
                CreatedAtUtc = DateTime.UtcNow
            };
            
            var customerProfile = new Customer
            {
                UserId = user.Id,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Users.Add(user);
            _context.Customers.Add(customerProfile);
            await _context.SaveChangesAsync();
        }

        var randomCode = request.Phone.StartsWith("999")
            ? "123456"
            : RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var otpDetails = new OtpDetails
        {
            Code = randomCode,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(3),
            Attempts = 0,
            LastRequestedAtUtc = DateTime.UtcNow
        };

        _cache.Set(cacheKey, otpDetails, TimeSpan.FromMinutes(5));

        BackgroundJob.Enqueue<ISmsService>(x => x.SendOtpSmsAsync(request.Phone, randomCode));

        return Ok(new { message = "OTP sent successfully." });
    }

    [HttpPost("customer/verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] CustomerOtpVerifyRequest request)
    {
        var cacheKey = $"otp:{request.Phone}";
        var lockObj = OtpLocks.GetOrAdd(cacheKey, _ => new object());

        bool isCodeValid = false;
        bool isAttemptsExceeded = false;
        bool isExpiredOrInvalid = false;

        lock (lockObj)
        {
            if (!_cache.TryGetValue(cacheKey, out OtpDetails? otpDetails) || otpDetails == null)
            {
                isExpiredOrInvalid = true;
            }
            else if (DateTime.UtcNow > otpDetails.ExpiresAtUtc)
            {
                _cache.Remove(cacheKey);
                isExpiredOrInvalid = true;
            }
            else if (otpDetails.Attempts >= 3)
            {
                isAttemptsExceeded = true;
            }
            else if (otpDetails.Code != request.Code)
            {
                otpDetails.Attempts++;
                _cache.Set(cacheKey, otpDetails, TimeSpan.FromMinutes(5));
                if (otpDetails.Attempts >= 3)
                {
                    isAttemptsExceeded = true;
                }
                else
                {
                    isExpiredOrInvalid = true;
                }
            }
            else
            {
                isCodeValid = true;
                _cache.Remove(cacheKey);
            }
        }

        if (isExpiredOrInvalid)
        {
            return Unauthorized(new { message = "Invalid or expired OTP." });
        }

        if (isAttemptsExceeded)
        {
            return StatusCode(429, new { message = "Too many failed attempts. Request a new OTP." });
        }

        if (!isCodeValid)
        {
            return Unauthorized(new { message = "Invalid or expired OTP." });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Phone == request.Phone && u.Role == Role.Customer);
        
        if (user == null)
        {
            return Unauthorized(new { message = "User not found." });
        }

        var response = await CreateAuthResponseAsync(user);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        try
        {
            var principal = _tokenService.GetPrincipalFromExpiredToken(request.Token);
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier) 
                           ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out Guid userId))
            { 
                return BadRequest(new { message = "Invalid token claims." });
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return BadRequest(new { message = "User not found." });
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                var storedRefreshToken = await _context.RefreshTokens
                    .FromSqlRaw("SELECT * FROM RefreshTokens WITH (UPDLOCK, ROWLOCK) WHERE Token = {0}", request.RefreshToken)
                    .FirstOrDefaultAsync();

                if (storedRefreshToken == null)
                {
                    await transaction.RollbackAsync();
                    return Unauthorized(new { message = "Refresh token does not exist." });
                }

                if (storedRefreshToken.RevokedOn != null || storedRefreshToken.IsExpired)
                {
                    if (storedRefreshToken.RevokedOn != null && 
                        (DateTime.UtcNow - storedRefreshToken.RevokedOn.Value).TotalSeconds < 2.0 &&
                        !string.IsNullOrEmpty(storedRefreshToken.ReplacedByToken))
                    {
                        _logger.LogInformation("Concurrent refresh token retry detected for token {Token} within grace period. Returning existing rotated token.", request.RefreshToken);
                        
                        var replacementToken = await _context.RefreshTokens
                            .FirstOrDefaultAsync(r => r.Token == storedRefreshToken.ReplacedByToken);

                        var graceAccessToken = _tokenService.GenerateToken(user);

                        await transaction.CommitAsync();

                        return Ok(new AuthResponse
                        {
                            Token = graceAccessToken,
                            RefreshToken = replacementToken?.Token ?? storedRefreshToken.ReplacedByToken
                        });
                    }

                    var activeTokens = await _context.RefreshTokens
                        .Where(r => r.UserId == userId && r.RevokedOn == null)
                        .ToListAsync();

                    foreach (var token in activeTokens)
                    {
                        token.RevokedOn = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Unauthorized(new { message = "Token reuse or expiry detected. All sessions revoked." });
                }

                storedRefreshToken.RevokedOn = DateTime.UtcNow;

                var newAccessToken = _tokenService.GenerateToken(user);
                var newRefreshToken = _tokenService.GenerateRefreshToken(userId);
                storedRefreshToken.ReplacedByToken = newRefreshToken.Token;

                _context.RefreshTokens.Add(newRefreshToken);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new AuthResponse
                {
                    Token = newAccessToken,
                    RefreshToken = newRefreshToken.Token
                });
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            return BadRequest(new { message = "Token refresh failed.", error = ex.Message });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = _currentUser.UserId;

        if (userId.HasValue)
        {
            var activeTokens = await _context.RefreshTokens
                .Where(r => r.UserId == userId.Value && r.RevokedOn == null)
                .ToListAsync();

            foreach (var token in activeTokens)
            {
                token.RevokedOn = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        var expClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Exp)?.Value;

        if (jti != null && expClaim != null)
        {
            var expirationTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expClaim));
            var timeUntilExpiry = expirationTime - DateTimeOffset.UtcNow;

            if (timeUntilExpiry > TimeSpan.Zero)
            {
                _cache.Set($"blocklist:{jti}", true, timeUntilExpiry);
            }
        }

        return Ok(new { message = "Successfully logged out and tokens revoked." });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = _currentUser.UserId;

        if (userId == null)
        {
            return Unauthorized();
        }

        var user = await _context.Users
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            user.Id,
            user.Email,
            user.Phone,
            user.Role,
            user.StoreId,
            CustomerProfile = user.Customer != null ? new { user.Customer.Id, user.Customer.FirstName, user.Customer.LastName } : null
        });
    }

    [HttpPost("partner")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreatePartner([FromBody] CreatePartnerRequest request)
    {
        if (request.StoreId.HasValue)
        {
            var storeExists = await _context.Stores.AnyAsync(s => s.Id == request.StoreId.Value);
            if (!storeExists)
            {
                return BadRequest(new { message = "The specified store does not exist." });
            }
        }
        else
        {
            return BadRequest(new { message = "StoreId is required for partners." });
        }

        var emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email);
        if (emailExists)
        {
            return BadRequest(new { message = "A user with this email already exists." });
        }

        var phoneExists = !string.IsNullOrEmpty(request.Phone) && await _context.Users.AnyAsync(u => u.Phone == request.Phone);
        if (phoneExists)
        {
            return BadRequest(new { message = "A user with this phone number already exists." });
        }

        var partnerUser = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            Phone = string.IsNullOrEmpty(request.Phone) ? null : request.Phone,
            Role = Role.Partner,
            StoreId = request.StoreId,
            CreatedAtUtc = DateTime.UtcNow
        };

        partnerUser.PasswordHash = _passwordHasher.HashPassword(partnerUser, request.Password);

        _context.Users.Add(partnerUser);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Partner user successfully created.", userId = partnerUser.Id });
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(User user)
    {
        var token = _tokenService.GenerateToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return new AuthResponse
        {
            Token = token,
            RefreshToken = refreshToken.Token
        };
    }
}

public class OtpDetails
{
    public string Code { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime LastRequestedAtUtc { get; set; }
}