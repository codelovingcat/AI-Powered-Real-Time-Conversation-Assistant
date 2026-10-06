using System.IdentityModel.Tokens.Jwt;\nusing System.Security.Claims;
using Conversa.Api.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Conversa.Api.Tests.Identity;

public sealed class JwtAccessTokenFactoryTests
{
    [Fact]
    public void Create_issues_a_signed_short_lived_token_for_the_user()
    {
        var keyBytes = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var factory = new JwtAccessTokenFactory(
            "conversa-test",
            "conversa-api",
            signingKey,
            TimeSpan.FromMinutes(15),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 6, 11, 0, 0, TimeSpan.Zero)));

        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var result = factory.Create(userId);

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(
            result.Token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "conversa-test",
                ValidateAudience = true,
                ValidAudience = "conversa-api",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateLifetime = false,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            },
            out var validatedToken);

        Assert.Equal(userId.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(SecurityAlgorithms.HmacSha256, ((JwtSecurityToken)validatedToken).Header.Alg);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 6, 11, 15, 0, TimeSpan.Zero),
            result.ExpiresAt);
    }

    [Fact]
    public void Create_rejects_an_empty_user_id()
    {
        var factory = new JwtAccessTokenFactory(
            "conversa-test",
            "conversa-api",
            new SymmetricSecurityKey(new byte[32]),
            TimeSpan.FromMinutes(15),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => factory.Create(Guid.Empty));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
