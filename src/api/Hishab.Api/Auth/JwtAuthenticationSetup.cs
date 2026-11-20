using Hishab.Application.Common.Security;
using Hishab.Infrastructure.Shops.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hishab.Api.Auth;

public static class JwtAuthenticationSetup
{
    public static IServiceCollection AddHishabJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from the validated JwtOptions (ValidateOnStart), not read eagerly from IConfiguration.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, clock) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep "sub", "tenant_id", "role" as issued
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // no "alg: none", no algorithm confusion
                    NameClaimType = HishabClaimTypes.UserId,
                    RoleClaimType = HishabClaimTypes.Role,
                    RequireExpirationTime = true,
                    ClockSkew = jwt.ClockSkew,

                    // Same clock as the issuer, so tests can expire tokens by advancing a fake clock.
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        return expires is { } exp && now < exp + parameters.ClockSkew
                            && (notBefore is not { } nbf || now >= nbf - parameters.ClockSkew);
                    },
                };
            });

        return services;
    }
}
