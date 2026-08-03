using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wasil.Api.DTOs.Auth;
using Wasil.Service.Interfaces;
using Wasil.Data;
using Wasil.Data.Entities;
using Wasil.Data.Enums;

namespace Wasil.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly WasilDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthController> _logger;
    public AuthController(
        WasilDbContext context, 
        ITokenService tokenService, 
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthController> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
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

        var token = _tokenService.GenerateToken(user);
        return Ok(new AuthResponse { Token = token });
    }
    
    [HttpPost("customer/request-otp")]
    public async Task<IActionResult> RequestOtp([FromBody] CustomerOtpRequest request)
    {
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

        var mockOtp = "123456"; 
        
        _logger.LogInformation("OTP for {Phone} is {OTP}", request.Phone, mockOtp);

        return Ok(new { message = "OTP sent successfully. (Check console for mock code)" });
    }

    [HttpPost("customer/verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] CustomerOtpVerifyRequest request)
    {
        if (request.Code != "123456") 
        {
            return Unauthorized(new { message = "Invalid or expired OTP." });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Phone == request.Phone && u.Role == Role.Customer);
        
        if (user == null)
        {
            return Unauthorized(new { message = "User not found." });
        }

        var token = _tokenService.GenerateToken(user);
        return Ok(new AuthResponse { Token = token });
    }
}