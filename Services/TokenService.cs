using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MG.Server.Entities;
using Microsoft.IdentityModel.Tokens;

namespace MG.Server.Services
{
    /// <summary>Bound from the "Jwt" configuration section. The signing Key must NOT be
    /// committed to source: in production supply it via the <c>Jwt__Key</c> environment
    /// variable; in Development a throwaway key is used (see Program.cs).</summary>
    public class JwtSettings
    {
        public string Issuer { get; set; } = "MultiGameX";
        public string Audience { get; set; } = "MultiGameX";
        public string Key { get; set; } = "";
        public int ExpireHours { get; set; } = 24;
    }

    /// <summary>Issues signed JWTs for authenticated users.</summary>
    public class TokenService
    {
        private readonly JwtSettings _settings;

        public TokenService(JwtSettings settings)
        {
            _settings = settings;
        }

        public string CreateToken(UserData user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim("name", user.Name ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(_settings.ExpireHours),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
