using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Auth;
using Microsoft.PowerPlatform.Dataverse.Client.Model;

namespace dvtui.Services;

public sealed class DataverseConnectionManager : IDisposable
{
    private readonly ServiceClient _client;
    private readonly string _environmentUrl;

    public DataverseConnectionManager(string url)
    {
        _environmentUrl = NormalizeEnvironmentUrl(url);
        var tokenCachePath = GetTokenCachePath(_environmentUrl);
        var tokenProvider = new MsalTokenProvider(
            _environmentUrl,
            tokenCachePath
        );
        var options = new ConnectionOptions
        {
            AuthenticationType =
                AuthenticationType.ExternalTokenManagement,
            ServiceUri = new Uri(_environmentUrl),
            AccessTokenProviderFunctionAsync =
                _ => tokenProvider.GetTokenAsync(CancellationToken.None),
            SkipDiscovery = true
        };

        SessionLog.Debug(
            "Dataverse.Client",
            "Token cache path=" + tokenCachePath
        );
        _client = new ServiceClient(options, deferConnection: true);
        var executor = new LoggingDataverseExecutor(
            new ServiceClientExecutor(_client)
        );
        QueryService = new DataverseQueryService(executor);
        SchemaService = new DataverseSchemaService(
            executor,
            _environmentUrl
        );
        SessionLog.Info(
            "Dataverse.Client",
            "Created deferred client for environment=" + _environmentUrl
        );
    }

    public string EnvironmentUrl => _environmentUrl;

    public DataverseQueryService QueryService { get; }

    public DataverseSchemaService SchemaService { get; }

    public void Connect()
    {
        SessionLog.Info(
            "Dataverse.Connect",
            "Starting connection to environment=" + _environmentUrl
        );
        try
        {
            _client.Connect();

            if( !_client.IsReady )
            {
                SessionLog.Warning(
                    "Dataverse.Connect",
                    "Client is not ready. lastError=" + _client.LastError
                        + " lastException=" + _client.LastException
                );
                throw new InvalidOperationException(
                    $"Connection failed: {_client.LastError}"
                );
            }

            SessionLog.Info(
                "Dataverse.Connect",
                "Connection ready. isReady=" + _client.IsReady
            );
        }
        catch( Exception ex )
        {
            SessionLog.Exception(
                "Dataverse.Connect",
                ex,
                "Connection failed for environment=" + _environmentUrl
                    + " lastError=" + _client.LastError
                    + " lastException=" + _client.LastException
            );
            throw;
        }
    }

    public void Dispose()
    {
        SessionLog.Info(
            "Dataverse.Client",
            "Disposing client for environment=" + _environmentUrl
        );
        _client.Dispose();
    }

    private static string GetTokenCachePath(string environmentUrl)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dvtui"
        );
        Directory.CreateDirectory(directory);
        var host = new Uri(environmentUrl).Host;
        var fileName = "token-cache-" + host + ".dat";
        return Path.Combine(directory, fileName);
    }

    private static string NormalizeEnvironmentUrl(string url)
    {
        url = url.Replace("http://", "").TrimEnd('/');
        if( url.StartsWith("https://") == false )
        {
            url = "https://" + url;
        }

        return url;
    }
}
