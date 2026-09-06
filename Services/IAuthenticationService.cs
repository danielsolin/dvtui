namespace Dvtui.Services;

public interface IAuthenticationService
{
    Task<string> GetAccessTokenAsync();
    Task<string> GetTokenByPasswordAsync(string username, string password);
}
