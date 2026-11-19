using System.ComponentModel.DataAnnotations;
namespace Application.DTOs;
public sealed class LoginDto
{
    [Required, StringLength(50), RegularExpression(@"^[a-zA-Z0-9_.-]{3,50}$")]
    public string Username { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}
