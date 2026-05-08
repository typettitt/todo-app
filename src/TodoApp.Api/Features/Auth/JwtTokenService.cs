using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using TodoApp.Api.Data.Entities;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Issues short-lived signed JWTs for the auth cookie. Uses HS256 with the key
/// resolved by <see cref="JwtKeyProvider"/>.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, JwtKeyProvider keyProvider)
{
    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(keyProvider.Key),
        SecurityAlgorithms.HmacSha256);

    public string IssueToken(User user, Guid sid, DateTimeOffset issuedAt, TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        var life = lifetime ?? _options.Lifetime;
        var notBefore = issuedAt.UtcDateTime;
        var expires = issuedAt.Add(life).UtcDateTime;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            // Server-side session id. JwtBearer resolves this against
            // AuthSessionService to enforce revocation; without it the token
            // fails validation.
            new Claim("sid", sid.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
