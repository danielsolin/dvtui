using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace Dvtui.Services;

public class OAuthAuthenticationService : IAuthenticationService
{
    private readonly IPublicClientApplication _clientApp;
    private readonly string[] _scopes;

    public OAuthAuthenticationService(
        string clientId,
        string tenantId,
        string[] scopes)
    {
        var authority = $"https://login.microsoftonline.com/{tenantId}";
        _clientApp = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(authority)
            .Build();
        _scopes = scopes;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        var accounts = await _clientApp.GetAccountsAsync();
        
        try
        {
            var result = await _clientApp.AcquireTokenSilent(
                _scopes,
                accounts.FirstOrDefault())
                .ExecuteAsync();
            return result.AccessToken;
        }
        catch (Exception)
        {
            var result = await _clientApp.AcquireTokenInteractive(
                _scopes)
                .ExecuteAsync();
            return result.AccessToken;
        }
    }

    public Task<string> GetTokenByPasswordAsync(
        string username,
        string password)
    {
        // ROPC is not implemented to prioritize browser flow.
        throw new NotSupportedException("Password flow is not supported.");
    }
}
