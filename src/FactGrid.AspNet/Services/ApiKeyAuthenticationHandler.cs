using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FactGrid.AspNet.Services;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        var configuredKey = Options.ApiKey
            ?? Context.RequestServices.GetRequiredService<IConfiguration>()
                .GetValue<string>("Auth:ApiKey");

        if (!string.IsNullOrEmpty(apiKey) && apiKey == configuredKey)
        {
            var identity = new ClaimsIdentity(SchemeName);
            identity.AddClaim(new Claim(ClaimTypes.Name, "factgrid-mcp"));
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public string? ApiKey { get; set; }
}
