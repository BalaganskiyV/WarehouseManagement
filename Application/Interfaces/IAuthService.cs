using Application.DTOs;
namespace Application.Interfaces;
public interface IAuthService
{
    Task<AuthenticatedUser?> LoginAsync(LoginDto input, CancellationToken ct);
    Task<AuthenticatedUser?> RegisterAsync(RegisterDto input, CancellationToken ct);
}
