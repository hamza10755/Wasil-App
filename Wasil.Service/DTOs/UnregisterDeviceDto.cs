using System.ComponentModel.DataAnnotations;

namespace Wasil.Service.DTOs;

public record UnregisterDeviceDto
{
    [Required]
    public string Token { get; init; } = string.Empty;
}
