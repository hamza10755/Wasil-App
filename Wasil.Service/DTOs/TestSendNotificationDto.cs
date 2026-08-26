using System.ComponentModel.DataAnnotations;

namespace Wasil.Service.DTOs;

public record TestSendNotificationDto
{
    [Required]
    public string Token { get; init; } = string.Empty;

    [Required]
    public string Title { get; init; } = string.Empty;

    [Required]
    public string Body { get; init; } = string.Empty;
}
