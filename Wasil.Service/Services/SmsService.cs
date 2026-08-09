using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wasil.Service.Interfaces;

namespace Wasil.Service.Services;

public class SmsService : ISmsService
{
    private readonly ILogger<SmsService> _logger;

    public SmsService(ILogger<SmsService> logger)
    {
        _logger = logger;
    }

    public async Task SendOtpSmsAsync(string phone, string code)
    {
        await Task.Delay(500);
        _logger.LogInformation("Background SMS Job: OTP for {Phone} is {OTP}", phone, code);
    }
}
