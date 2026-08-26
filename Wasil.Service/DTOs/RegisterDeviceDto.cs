using System.ComponentModel.DataAnnotations;

namespace Wasil.Service.DTOs;

public record RegisterDeviceDto
{
    [Required]
    public string Token { get; init; } = string.Empty;

    public string Platform { get; init; } = "web";
}
