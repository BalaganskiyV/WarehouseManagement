using Application.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed class AuthService(AppDbContext db, IPasswordHasher<User> hasher) : IAuthService
{
    public async Task<AuthenticatedUser?> LoginAsync(LoginDto input, CancellationToken ct)
    {
        var username = NormalizeUsername(input.Username);
        var user = await db.Users.Include(user => user.Role)
            .SingleOrDefaultAsync(user => user.Username == username, ct);

        if (user is null)
        {
            return null;
        }

        PasswordVerificationResult result;
        try
        {
            result = hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password);
        }
        catch (FormatException)
        {
            return null;
        }

        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            await db.SaveChangesAsync(ct);
        }

        return new AuthenticatedUser(user.Id, user.Username, user.Role.Name);
    }

    public async Task<AuthenticatedUser?> RegisterAsync(RegisterDto input, CancellationToken ct)
    {
        var username = NormalizeUsername(input.Username);
        if (await UsernameExistsAsync(username, ct))
        {
            return null;
        }

        var role = await db.Roles.SingleAsync(role => role.Name == "Clerk", ct);
        var user = new User
        {
            Username = username,
            Email = input.Email.Trim(),
            Role = role
        };

        user.PasswordHash = hasher.HashPassword(user, input.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await UsernameExistsAsync(username, ct))
            {
                return null;
            }

            throw;
        }

        return new AuthenticatedUser(user.Id, user.Username, role.Name);
    }

    private Task<bool> UsernameExistsAsync(string username, CancellationToken ct)
    {
        return db.Users.AnyAsync(user => user.Username == username, ct);
    }

    private static string NormalizeUsername(string username)
    {
        return username.Trim().ToLowerInvariant();
    }
}
