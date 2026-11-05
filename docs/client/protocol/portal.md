# Portal: new client

How the new client implements [Portal](../../../gw2-research/docs/protocol/portal.md). The retail behaviour and its evidence are in that note.

## Custom client portal login (2026-10-06)

`Gw2.Client.Net.Portal.PortalLogin` (run by the retired `Gw2.LiveLogin` tool through `Gw2AccountLogin`, credentials in
gitignored `captures/local/live-account.json`) logged in to the real portal and received a game token:

A `/Presence/UserInfo` push arrives between requests; `StsConnection` skips pushes.

## Steam login and the account login ids

The client remake sets `SteamAppId`/`SteamGameId` to the app id read from the exe before `SteamAPI_InitFlat`, so it
needs no Steam launch. It offers Steam when `HKCU\Software\Valve\Steam\ActiveProcess` names a live `pid` with a
non-zero `ActiveUser` and `HKCU\Software\Valve\Steam\Apps\1284210` exists (`SteamAvailability`).

`SteamApiDeployment` reads the archive name, the app id and the two names from the exe at run time; the client writes
the library to the same temp folder and asks the running Steam client for the ticket (`SteamAuthTicket`).
