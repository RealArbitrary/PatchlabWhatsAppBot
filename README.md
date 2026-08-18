# PatchlabWhatsAppBot

A WhatsApp auto-response bot for [Patchlab](https://github.com/) (IT support & web dev), built on ASP.NET Core and Meta's WhatsApp Cloud API.

> **Note on the name:** the project started life as a Twilio Sandbox proof of concept, then migrated fully to Meta's Cloud API to avoid Twilio's per-message markup. Twilio is no longer used anywhere in the codebase.

## What it does

Patchlab's WhatsApp number receives messages from two kinds of senders:

- **LSF Personeel**, staff at a school (a contact of the developer's) who message in to log an issue and get a ticket number. *(implemented)*
- **Everyone else**, existing clients with an issue, or new clients looking for something. *(not yet implemented, see Roadmap)*

The bot runs a simple state machine conversation per phone number and replies automatically. A human can also take over a conversation manually from the same number/thread, since the number is no longer usable in the normal WhatsApp Business consumer app once it's on the Cloud API. That manual takeover tool now lives in the separate `PatchlabTicketing` project, not here (see "Related projects" below).

## Current flow (LSF Personeel)

```
"hi"                → "Hi! What's your issue?"
<issue text>        → "Thanks, ticket TCKT-0001 created. We'll get back to you."
```

The ticket number is a real, persisted SQL identity value, not a timestamp placeholder.

## Stack

- ASP.NET Core Web API, .NET 10
- Meta WhatsApp Cloud API (Graph API `v21.0`, due for a bump to `v26.0` before Meta drops `v21.0` support)
- SQL Server 2025, accessed via Dapper (chosen over EF Core for this project, lighter weight, no migration ceremony)
- In-memory `ConcurrentDictionary` based conversation store (POC grade, see Roadmap)
- ngrok for local and production webhook tunneling
- NSSM to run the bot as a Windows service in production

## Project structure

```
PatchlabWhatsAppBot/
├── Controllers/
│   └── WhatsAppWebhookController.cs   # GET verification handshake + POST message handling
├── Conversations/
│   ├── ConversationState.cs           # enum: New, AwaitingIssue, AwaitingClientType (unused), Completed
│   ├── ConversationSession.cs         # per-number session: State, IssueText, TicketNumber
│   └── ConversationStore.cs           # in-memory store keyed by phone number
├── Tickets/
│   └── TicketRepository.cs            # ITicketRepository + Dapper implementation, writes to SQL Tickets table
├── WhatsApp/
│   ├── MetaWhatsAppOptions.cs         # bound config: PhoneNumberId, AccessToken, VerifyToken
│   ├── IWhatsAppSender.cs
│   └── MetaWhatsAppSender.cs          # sends replies via graph.facebook.com
└── Program.cs
```

`Conversations/` is transport agnostic, it has no idea whether messages came in via Twilio or Meta. `WhatsApp/` is where the Meta Cloud API specific plumbing lives. This split is what made the Twilio to Meta migration a controller and transport layer rewrite only, not a rewrite of the actual conversation logic.

`Tickets/` is intentionally minimal. This project only ever inserts ticket rows. Everything that reads or updates tickets (dashboard, status changes, manual reply flow) lives in `PatchlabTicketing`, a separate app reading the same database. See "Related projects" below for why.

## Data persistence

Tickets are stored in a SQL Server database named `Patchlab`, in a single `Tickets` table:

```sql
CREATE TABLE Tickets (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    TicketNumber     AS ('TCKT-' + RIGHT('0000' + CAST(Id AS VARCHAR(10)), 4)) PERSISTED,
    CellphoneNumber  NVARCHAR(20)   NOT NULL,
    Issue            NVARCHAR(MAX)  NOT NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    Status           NVARCHAR(20)   NOT NULL DEFAULT 'Open'
);
```

`TicketNumber` is a persisted computed column derived from `Id`. There is no separate counter or sequence table, so there is no risk of formatting drift between this bot and `PatchlabTicketing`.

The SQL connection string lives in `config.json`, under the `SqlConnectionString` key (see Configuration below). Authentication is Windows integrated auth. The account the bot's NSSM service runs under needs an explicit SQL Server login. If the service runs as `LocalSystem`, that identity presents to SQL Server as `NT AUTHORITY\SYSTEM` for local connections, not the machine account name.

## Configuration

This project reads configuration from `config.json` via `WhatsAppBotConfig.SharedConfig.Load()`, not `dotnet user-secrets`. `config-example.json` documents the required shape:

```json
{
  "PhoneNumberId": "PASTE_PHONE_NUMBER_ID_HERE",
  "AccessToken": "PASTE_ACCESS_TOKEN_HERE",
  "VerifyToken": "PASTE_VERIFY_TOKEN_HERE",
  "SqlConnectionString": "PASTE_SQL_CONNECTION_STRING_HERE"
}
```

Copy `config-example.json` to `config.json` and fill in real values. `config.json` should never be committed with real credentials in it.

## Setup

1. **Create a Meta app** (Business type) in [Meta for Developers](https://developers.facebook.com/), add the WhatsApp product.
2. **Register your WhatsApp number** with the Cloud API.
   - If the number is already active in the normal WhatsApp Business consumer app, you'll need to disconnect/migrate it first. This is a one way move, the number can no longer be used in the consumer app afterwards. Export any chat history you want to keep before doing this.
3. **Generate a permanent access token.** The token shown on the API Setup / quickstart page is often labeled "permanent" but may actually be a 24 hour temporary token, verify with [Meta's Access Token Debugger](https://developers.facebook.com/tools/debug/accesstoken/) (`Expires: Never` plus `Data Access Expires: Never` means genuinely permanent). For a real permanent token, generate one via **Business Settings → System Users**.
4. **Create the SQL database and table** on the machine that will run the service (see Data persistence above).
5. **Set up `config.json`** as described in Configuration above.
6. **Run the app**, then tunnel it with ngrok, targeting the HTTPS port explicitly:
```
   ngrok http https://localhost:7055
```
   (`ngrok http 7055` alone won't speak TLS to the dev cert HTTPS endpoint and will silently hang.)
7. **In Meta's dashboard**, under WhatsApp → Configuration, set:
   - Callback URL: `https://<your-ngrok-subdomain>.ngrok-free.app/webhook/whatsapp`
   - Verify token: the same string set as `VerifyToken` in `config.json`
   - Subscribe to the `messages` field
8. Click **Verify and save**, this triggers Meta's one time GET handshake against the running app.

## Running in production

The bot is deployed to a dedicated machine (referred to internally as "the server") and does not run interactively. Three pieces work together there:

- **`PatchlabWhatsAppBot`**, this project, run as a Windows service via **NSSM**. NSSM's default `AppExit` setting restarts the service on any exit, including clean ones. If that's not the desired behavior, override it with `nssm set <ServiceName> AppExit Default Exit`.
- **ngrok**, kept running as a persistent tunnel so the public webhook URL stays stable across restarts.
- **`PatchlabNgrokSync`**, a small companion service that keeps Meta's registered Callback URL in sync whenever ngrok's public URL changes.

Deployment is automatic: a GitHub Actions runner on the server picks up pushes to this repo, builds, and republishes. There is no manual `git pull` step. Development happens on a separate machine, with a build check happening there before every commit and push.

## Related projects

Ticket handling beyond "create a ticket row" (dashboard viewing, status updates, the manual takeover reply tool) lives in a separate app, `PatchlabTicketing`, not in this repo. That app is an ASP.NET Core Web API plus React dashboard, reading and updating the same SQL `Tickets` table this bot writes to.

The two apps do not talk to each other over HTTP. The database is the interface. This bot has zero new endpoints or CORS surface as a result. The tradeoff is that dashboard updates are not instant, `PatchlabTicketing` polls the database every few seconds rather than receiving a push notification when a new ticket is written. That was a deliberate choice, not a placeholder, live push (SignalR) was considered and declined for now.

Keeping the two apps separate means this bot stays narrowly scoped to "receive message, reply", and doesn't need redeploying just because dashboard UI changes.

## Known gotchas

- `.NET 10`'s built in OpenAPI support currently fails to compile against `Microsoft.OpenApi` 3.x (`CS0200`). Pin to `Microsoft.OpenApi 2.3.9`, or drop Swagger entirely, it isn't adding value on a webhook only service.
- Meta's webhook GET verification (`hub.challenge`) must be echoed back as plain text, not wrapped in JSON.
- Meta enforces a 24 hour customer service window: free form text replies are only allowed within 24 hours of the customer's last message. Outside that window, business initiated messages require pre approved templates. This mainly affects manual dashboard testing (messaging yourself first), it shouldn't affect the real bot flow, since customers always message in first.
- Meta's "service conversations" (replying to inbound messages) are currently free, but Meta has a pricing change coming October 1, 2026 making service/utility messages inside the customer service window chargeable.
- Unverified Cloud API apps are capped at 250 unique customers messaged per rolling 24 hours. Business Verification removes this cap, not yet done, deferred until/unless the limit is hit.
- PowerShell's `curl` alias mangles curl style flags, use `-Headers @{...}` and `-UseBasicParsing` instead.
- NSSM services need an explicit `AppDirectory`/startup directory set, or relative path lookups can silently fail.
- Services.msc showing "Running" does not confirm health, verify with a live request or log check.
- Any secret pasted into a chat or AI session should be treated as compromised and rotated.

## Roadmap

With SQL backed ticket persistence now in place, the outstanding work on this project specifically is:

- [ ] **"Other clients" branch**, routing non LSF senders into existing client with an issue versus new client looking for something flows. Should be designed with the eventual manual takeover flow (now in `PatchlabTicketing`) in mind.
- [ ] **Business logic change under discussion: moving to a card based structure** for the conversation flow. Not yet decided whether this changes how outbound WhatsApp API calls are made (for example, sending interactive list/button messages instead of free text) or is purely an internal state representation change. Needs its own writeup once decided, since it could affect `MetaWhatsAppSender` and the webhook payload parsing, not just `Conversations/`.

Broader production hardening items, tracked but not blocking day to day development:

- [ ] CI/CD deploy ordering flaw, a failed `dotnet publish` leaves the service stopped indefinitely with no alert. Applies to this project, `PatchlabNgrokSync`, and `PatchlabTicketing`.
- [ ] Replace the in memory `ConversationStore` with persistent storage, it is currently wiped on every service restart.
- [ ] Read and log Meta's error response body on send failures instead of letting `EnsureSuccessStatusCode()` throw blind.
- [ ] Replace manual `JsonElement` parsing of Meta's webhook payload with strongly typed DTOs and `TryGetProperty` guards throughout.
- [ ] Bump Graph API calls from `v21.0` to `v26.0` before Meta removes `v21.0` support.
- [ ] Switch this service's NSSM account off `LocalSystem` to a dedicated least privilege local account. Should happen before going live, does not affect day to day development.
- [ ] Remove the dead `MetaWhatsAppOptions.SectionName` code, cosmetic, low priority.
- [ ] Delete the stray `PatchlabTwilioBot.csproj.Backup.tmp` file at the repo root.

## Security notes for anyone cloning this

- **Never commit real credentials.** `config.json` in this repo should never contain real values, only `config-example.json` should be committed, with placeholders.
- Use environment variables, Azure Key Vault, or an equivalent secret store in production rather than a plain `config.json` on disk, if that becomes a concern.
- If `PatchlabWhatsAppBot.csproj.Backup.tmp` or similar stray files exist in your working copy, don't commit them, clean up before pushing.