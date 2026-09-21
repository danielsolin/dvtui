using Microsoft.Identity.Client;

namespace dvtui.Services;

/// <summary>
/// Acquires Dataverse OAuth tokens using MSAL and persists the MSAL user
/// token cache to a file. File persistence is used instead of the built-in
/// OAuth token cache store because that store depends on a system keyring
/// (Secret Service) on Linux, which is not reliably available here.
/// </summary>
internal sealed class MsalTokenProvider
{
    // Microsoft's shared public Power Platform client. It has no secret and
    // allows the http://localhost loopback redirect used for interactive
    // login.
    private const string ClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    private const string RedirectUri = "http://localhost";

    private readonly IPublicClientApplication _app;
    private readonly string[] _scopes;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _acquireLock = new(1, 1);

    public MsalTokenProvider(
        string environmentUrl,
        string cachePath
    )
    {
        _cachePath = cachePath;
        _scopes = new[] { environmentUrl + "/user_impersonation" };

        _app = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithRedirectUri(RedirectUri)
            .Build();
        _app.UserTokenCache.SetBeforeAccess(OnBeforeCacheAccess);
        _app.UserTokenCache.SetAfterAccess(OnAfterCacheAccess);

        SessionLog.Debug(
            "Dataverse.Auth",
            "MSAL provider ready cache=" + cachePath
        );
    }

    public async Task<string> GetTokenAsync(
        CancellationToken cancellationToken
    )
    {
        await _acquireLock.WaitAsync( cancellationToken );
        try
        {
            return await AcquireTokenInternalAsync();
        }
        finally
        {
            _acquireLock.Release();
        }
    }

    private async Task<string> AcquireTokenInternalAsync()
    {
        var accounts = await _app.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if( account != null )
        {
            try
            {
                var silent =
                    await _app.AcquireTokenSilent( _scopes, account )
                        .ExecuteAsync();
                SessionLog.Info(
                    "Dataverse.Auth",
                    "Silent token acquired for " + account.Username
                );
                return silent.AccessToken;
            }
            catch( MsalUiRequiredException )
            {
                // Stale cache or pending consent; fall back to interactive.
            }
        }

        SessionLog.Info(
            "Dataverse.Auth",
            "Interactive login required"
            + (account != null ? " for " + account.Username : string.Empty)
        );
        var result = await _app
            .AcquireTokenInteractive( _scopes )
            .ExecuteAsync();
        return result.AccessToken;
    }

    private void OnBeforeCacheAccess( TokenCacheNotificationArgs args )
    {
        var bytes = LoadCache();
        if( bytes != null )
        {
            args.TokenCache.DeserializeMsalV3( bytes );
        }
    }

    private void OnAfterCacheAccess( TokenCacheNotificationArgs args )
    {
        if( args.HasStateChanged )
        {
            SaveCache( args.TokenCache.SerializeMsalV3() );
        }
    }

    private byte[]? LoadCache()
    {
        try
        {
            return File.Exists( _cachePath )
                ? File.ReadAllBytes( _cachePath )
                : null;
        }
        catch( IOException ex )
        {
            SessionLog.Warning(
                "Dataverse.Auth",
                "Token cache read failed: " + ex.Message
            );
            return null;
        }
    }

    private void SaveCache( byte[] cache )
    {
        try
        {
            var directory = Path.GetDirectoryName( _cachePath );
            if( !string.IsNullOrEmpty( directory ) )
            {
                Directory.CreateDirectory( directory );
            }
            File.WriteAllBytes( _cachePath, cache );
        }
        catch( IOException ex )
        {
            SessionLog.Warning(
                "Dataverse.Auth",
                "Token cache write failed: " + ex.Message
            );
        }
    }
}
