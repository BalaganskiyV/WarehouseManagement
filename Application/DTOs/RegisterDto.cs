using System.ComponentModel.DataAnnotations;
namespace Application.DTOs;
public sealed class RegisterDto
{
    [Required, StringLength(50), RegularExpression(@"^[a-zA-Z0-9_.-]{3,50}$")]
    public string Username { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12)] public string Password { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}
