using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Wasil.Api.Filters;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        string? token = httpContext.Request.Query["token"].ToString();

        if (!string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[HangfireAuth] Found token in query string.");
            httpContext.Response.Cookies.Append("hangfire_token", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(1)
            });
        }
        else
        {
            token = httpContext.Request.Cookies["hangfire_token"];
            if (!string.IsNullOrEmpty(token))
            {
                Console.WriteLine("[HangfireAuth] Found token in cookie.");
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[HangfireAuth] No token found in query or cookie. Returning 401.");
            return false;
        }

        try
        {
            var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
            var jwtKey = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured.");
            var jwtIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured.");
            var jwtAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured.");

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out _);

            var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value;
            Console.WriteLine($"[HangfireAuth] Token validated successfully. Role: {roleClaim}");
            
            bool isAdmin = roleClaim == "Admin";
            if (!isAdmin)
            {
                Console.WriteLine("[HangfireAuth] Access denied: User is not an Admin.");
            }
            return isAdmin;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HangfireAuth] Token validation failed: {ex.Message}");
            httpContext.Response.Cookies.Delete("hangfire_token");
            return false;
        }
    }
}