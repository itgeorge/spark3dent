# Google OAuth Login (Retired)

Spark3Dent no longer uses the former Google OAuth gate on `spark3dent.com`.

## Current production auth

Requests now flow directly through Caddy to the ASP.NET app:

```text
User → Caddy → ASP.NET app
```

The application-level organization/password login is responsible for access control.

## Historical note

The previous Hetzner deployment used `oauth2-proxy` plus Caddy `forward_auth` to require a Google login before users could reach the app. That layer was removed after the built-in user/password system became mandatory.

If cleaning up Google Cloud Console, check the old OAuth client and remove the Spark3Dent entries if it is not used elsewhere:

https://console.cloud.google.com/auth/clients/1098391572597-pomqof9b8mvo0n3b6kdm20pdmmi4a1t0.apps.googleusercontent.com?project=spark3dent

Old entries to remove if present:

- Authorized redirect URI: `https://spark3dent.com/oauth2/callback`
- Authorized JavaScript origin: `https://spark3dent.com`
