# Architecture

High-level notes on how dvtui is built. Keep this document lean and add
sections as the design grows.

## Authentication and token cache

The application signs in with Microsoft's shared public Power Platform
client (MSAL). It uses `AuthenticationType.ExternalTokenManagement` so
dvtui owns the MSAL `IPublicClientApplication` directly. The provider
lives in `Services/MsalTokenProvider.cs`.

- Client ID: `51f81489-12ee-4a9e-aaae-a2591f45987d` (public, no secret)
- Authority: `AzureAdMultipleOrgs`
- Redirect URI: `http://localhost`
- Scope: `<environment-url>/user_impersonation`

The MSAL user token cache is serialized (`SerializeMsalV3`) and stored in
a plain file, one per environment host, under the user's application data
folder (on Linux, `~/.config/dvtui/`):

```text
~/.config/dvtui/token-cache-<host>.dat
```

File persistence is used because the built-in OAuth cache store depends
on a system keyring (Secret Service) on Linux, which is not reliably
available. Delete the file for a host to sign out of that environment.
