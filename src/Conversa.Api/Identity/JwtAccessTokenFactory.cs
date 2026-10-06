using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Conversa.Api.Identity;

public sealed class JwtAccessTokenFactory(
    string issuer,
    string audience,
    SymmetricSecurityKey signingKey,
    TimeSpan lifetime,
    TimeProvider timeProvider)
{
    private readonly SigningCredentials _credentials =
        new(signingKey, SecurityAlgorithms.HmacSha256);

    public AccessTokenResult Create(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A valid user id is required.", nameof(userId));
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(lifetime);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _credentials);

        return new AccessTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
