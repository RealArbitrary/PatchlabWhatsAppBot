# PatchlabWhatsAppBot

A WhatsApp auto-response bot for [Patchlab](https://github.com/) (IT support & web dev), built on ASP.NET Core and Meta's WhatsApp Cloud API.

> **Note on the name:** the project started life as a Twilio Sandbox proof of concept, then migrated fully to Meta's Cloud API to avoid Twilio's per-message markup. Twilio is no longer used anywhere in the codebase.

## What it does

Patchlab's WhatsApp number receives messages from two kinds of senders:

- **LSF Personeel**, staff at a school (a contact of the developer's) who message in to log an issue and get a ticket number, or check on one already logged. *(implemented)*
- **Everyone else**, existing clients with an issue, or new clients looking for something. *(not yet implemented, see Roadmap)*

The bot runs a card-driven conversation state machine per phone number and replies automatically, using WhatsApp's interactive button/list messages wherever there's a fixed set of choices, rather than relying on the teacher typing free text correctly. A human can also take over a conversation manually from the same number/thread, since the number is no longer usable in the normal WhatsApp Business consumer app once it's on the Cloud API. That manual takeover tool lives in the separate `PatchlabTicketing` project, not here (see "Related projects" below).

## Current flow (LSF Personeel)

```
"hi"                       → card: "Log a ticket" / "Check on existing ticket"

Log a ticket (new number)  → asks name/surname → area → issue description
                            → ticket created, confirmation sent

Log a ticket (known number) → card: "Use saved details" / "Update my details"
                             → skips straight to area (name/surname already known)

Check on existing ticket    → card: list of this number's ticket IDs
                             → pulls latest status
                             → card: "I'm happy" / "Log another ticket" / "Not resolved to my liking"
                             → unhappy branch collects a reason and notifies staff
```

Returning numbers are recognised via the `Customers` table (see Data persistence below), so a teacher who has logged a ticket before never has to retype their name and surname — only re-confirm or update it.

## Stack

- ASP.NET Core Web API, .NET 10
- Meta WhatsApp Cloud API (Graph API `v21.0`, due for a bump to `v26.0` before Meta drops `v21.0` support)
- SQL Server 2025, accessed via **EF Core** (migrated from Dapper — see "Database & migrations" below for why and how)
- In-memory `ConcurrentDictionary` based conversation store (POC grade, see Roadmap)
- ngrok for local and production webhook tunneling
- NSSM to run the bot as a Windows service in production (see "Deploying to a server" — this is the tooling currently in use, not a hard requirement of the project itself)

## Project structure

```
PatchlabWhatsAppBot/
├── Controllers/
│   └── WhatsAppWebhookController.cs   # GET verification handshake + POST message handling, full state machine
├── Conversations/
│   ├── ConversationState.cs           # enum of every state in the flow above
│   ├── ConversationSession.cs         # per-number session: state, collected name/surname/area/issue, selected ticket
│   └── ConversationStore.cs           # in-memory store keyed by phone number
├── Customers/
│   └── CustomerRepository.cs          # ICustomerRepository, EF-backed — find/upsert the returning-customer profile
├── Tickets/
│   └── TicketRepository.cs            # ITicketRepository, EF-backed — create tickets, list by number, latest status
├── Staff/
│   └── WhatsAppStaffNotifier.cs       # IStaffNotifier — messages staff on new tickets and unhappy-ticket reports
├── Data/
│   └── PatchlabDbContext.cs           # EF Core DbContext + entity configuration for Tickets and Customers
├── Migrations/                        # EF Core migrations, generated via Add-Migration — see "Database & migrations"
├── WhatsApp/
│   ├── MetaWhatsAppOptions.cs         # bound config: PhoneNumberId, AccessToken, VerifyToken, staff notify number
│   ├── IWhatsAppSender.cs             # text + interactive button/list sends
│   └── MetaWhatsAppSender.cs          # sends replies via graph.facebook.com
└── Program.cs
```

`Conversations/` is transport agnostic, it has no idea whether messages came in via Twilio or Meta. `WhatsApp/` is where the Meta Cloud API specific plumbing lives. This split is what made the Twilio to Meta migration a controller and transport layer rewrite only, not a rewrite of the actual conversation logic.

`Tickets/` and `Customers/` are intentionally minimal — this project only ever inserts/reads what its own conversation flow needs. Everything else (dashboard, status changes, manual reply flow) lives in `PatchlabTicketing`, a separate app reading the same database. See "Related projects" below for why.

## Data persistence

Tickets and known customers are stored in a SQL Server database named `Patchlab`, managed via EF Core migrations rather than hand-written SQL. The two tables:

```sql
CREATE TABLE Tickets (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    TicketNumber     AS ('TCKT-' + RIGHT('0000' + CAST(Id AS VARCHAR(10)), 4)) PERSISTED,
    CellphoneNumber  NVARCHAR(20)   NOT NULL,
    Issue            NVARCHAR(MAX)  NOT NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    Status           NVARCHAR(20)   NOT NULL DEFAULT 'Open'
);

CREATE TABLE Customers (
    CellphoneNumber  NVARCHAR(20)   NOT NULL PRIMARY KEY,
    FirstName        NVARCHAR(100)  NOT NULL,
    LastName         NVARCHAR(100)  NOT NULL,
    Area             NVARCHAR(200)  NULL,
    CreatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
```

`TicketNumber` is a persisted computed column derived from `Id`. There is no separate counter or sequence table, so there is no risk of formatting drift between this bot and `PatchlabTicketing`. `Customers` is keyed on `CellphoneNumber` and joins directly onto `Tickets.CellphoneNumber` — it exists purely so the bot can recognise a returning teacher and skip re-asking for their name and surname.

The SQL connection string lives in `config.json`, under the `SqlConnectionString` key (see Configuration below). Authentication is Windows integrated auth. The account the bot's service runs under needs an explicit SQL Server login. If the service runs as `LocalSystem`, that identity presents to SQL Server as `NT AUTHORITY\SYSTEM` for local connections, not the machine account name.

## Database & migrations

This project uses **EF Core migrations**, not hand-run `.sql` scripts, for schema changes. This was a deliberate move away from Dapper + manual SQL, specifically so that a schema change made on a dev machine doesn't require manually recreating it on every other environment by hand.

**Making a schema change (day to day):**
1. Change the entity classes / `OnModelCreating` in `Data/PatchlabDbContext.cs`.
2. In Visual Studio's Package Manager Console (make sure the "Default project" dropdown is set to this project):
   ```
   Add-Migration SomeDescriptiveName
   ```
3. Review the generated file under `Migrations/` — for anything touching a table that predates EF Core, or a rename/data-preserving change, do not assume the generated `Up()`/`Down()` are safe as-is, check them.
4. Test it locally:
   ```
   Update-Database
   ```
5. Commit the migration files and push. The deploy pipeline applies pending migrations automatically (see below) — no manual `.sql` execution on any server, ever, going forward.

**One-time gotcha, already handled, documented here for the next environment this gets deployed to:** any table that existed *before* EF Core was introduced (in this project's case, `Tickets`) will already exist on a target database the first time migrations run there. The very first migration (`InitialCreate`) must not try to `CREATE TABLE` anything that already exists — check its `Up()`/`Down()` bodies only contain genuinely new objects (new tables, new indexes) before that first migration ever runs against a pre-existing database. This only matters once per environment, on the very first migration run against it.

## Configuration

This project reads configuration from `config.json` via `WhatsAppBotConfig.SharedConfig.Load()`, not `dotnet user-secrets`. `config-example.json` documents the required shape:

```json
{
  "PhoneNumberId": "PASTE_PHONE_NUMBER_ID_HERE",
  "AccessToken": "PASTE_ACCESS_TOKEN_HERE",
  "VerifyToken": "PASTE_VERIFY_TOKEN_HERE",
  "SqlConnectionString": "PASTE_SQL_CONNECTION_STRING_HERE",
  "RussellCellphoneNumber": "PASTE_STAFF_WHATSAPP_NUMBER_HERE"
}
```

Copy `config-example.json` to `config.json` and fill in real values. `config.json` should never be committed with real credentials in it. `RussellCellphoneNumber` (and any similar staff-notification number) should be in the same plain digits format WhatsApp itself uses for `from` on inbound messages — no `+`, no spaces.

## Setup (local development)

1. **Create a Meta app** (Business type) in [Meta for Developers](https://developers.facebook.com/), add the WhatsApp product.
2. **Register your WhatsApp number** with the Cloud API.
   - If the number is already active in the normal WhatsApp Business consumer app, you'll need to disconnect/migrate it first. This is a one way move, the number can no longer be used in the consumer app afterwards. Export any chat history you want to keep before doing this.
3. **Generate a permanent access token.** The token shown on the API Setup / quickstart page is often labeled "permanent" but may actually be a 24 hour temporary token, verify with [Meta's Access Token Debugger](https://developers.facebook.com/tools/debug/accesstoken/) (`Expires: Never` plus `Data Access Expires: Never` means genuinely permanent). For a real permanent token, generate one via **Business Settings → System Users**.
4. **Create the SQL database** (empty is fine — tables come from migrations, see above).
5. **Set up `config.json`** as described in Configuration above.
6. **Apply migrations locally:** `Update-Database` in the Package Manager Console.
7. **Run the app**, then tunnel it with ngrok, targeting the HTTPS port explicitly:
   ```
   ngrok http https://localhost:7055
   ```
   (`ngrok http 7055` alone won't speak TLS to the dev cert HTTPS endpoint and will silently hang.)
8. **In Meta's dashboard**, under WhatsApp → Configuration, set:
   - Callback URL: `https://<your-ngrok-subdomain>.ngrok-free.app/webhook/whatsapp`
   - Verify token: the same string set as `VerifyToken` in `config.json`
   - Subscribe to the `messages` field
9. Click **Verify and save**, this triggers Meta's one time GET handshake against the running app.

## Deploying to a server

The steps below are written generically since the exact hosting target may change — what matters is the shape of the deployment, not the specific tool. Whatever's used, it needs to satisfy four things: the app runs continuously as a background process, restarts automatically if it (or the machine) goes down, has a stable public HTTPS endpoint Meta can reach, and applies pending database migrations before serving new code.

1. **Pick a process manager** to keep the app running as a background service rather than an interactive console app. On Windows, [NSSM](https://nssm.cc/) (currently in use) wraps any executable as a proper Windows service — install, point it at the published `.exe`, set it to auto-start, done. Any equivalent works (a native Windows Service, a systemd unit on Linux, a container orchestrator, etc.) — the app itself doesn't care, it's just `dotnet run`/a published executable underneath.
2. **Publish the app** to a stable folder the process manager points at:
   ```
   dotnet publish -c Release -o <publish folder>
   ```
3. **Place a real `config.json`** next to the published output, filled in with that environment's real values (see Configuration above). This file is intentionally not part of the published build output and not committed to source control — it has to be created by hand once per environment and then left alone; publishing again won't touch or delete it.
4. **Apply database migrations** against that environment's database before starting the app on new code, so the schema is never behind what the code expects:
   ```
   dotnet ef database update --connection "<that environment's connection string>"
   ```
   If running this against a published (not source) folder, `dotnet-ef` may need pointing directly at the built assembly rather than a project file — `--assembly <path-to-dll> --startup-assembly <path-to-dll>` instead of `--project`.
5. **Expose a stable public HTTPS URL** Meta's webhook can reach. A tunnel (ngrok or similar) works for this without needing a public IP/domain of your own, provided something keeps the tunnel's URL in sync with what's registered in Meta's dashboard whenever it changes (see `PatchlabNgrokSync` under "Related projects").
6. **Start the service.** Confirm it's actually healthy with a live request or log check — a process manager reporting "running" only means the process didn't crash on launch, not that it's serving correctly.

**Automating the above (recommended once steps 1–6 have been done manually once):** a CI/CD pipeline triggered on push can run steps 2, 4, and 6 automatically — publish, apply pending migrations, restart the service — so that deploying a change is just a `git push`, with no manual file copying or SQL running on the target machine ever again. Two things worth knowing if setting this up on a self-hosted runner:
- A tool installed via `dotnet tool install --global` during one pipeline step won't automatically be on `PATH` for the *next* step unless explicitly appended to the runner's path for the rest of that job.
- If the runner is Windows-based, check which PowerShell is actually installed (`powershell` vs `pwsh` — the latter is PowerShell 7/Core and isn't guaranteed to be present) before writing pipeline steps that specify a shell explicitly.

## Related projects

Ticket handling beyond "create a ticket row" (dashboard viewing, status updates, the manual takeover reply tool) lives in a separate app, `PatchlabTicketing`, not in this repo. That app is an ASP.NET Core Web API plus React dashboard, reading and updating the same SQL `Tickets` table this bot writes to.

The two apps do not talk to each other over HTTP. The database is the interface. This bot has zero new endpoints or CORS surface as a result. The tradeoff is that dashboard updates are not instant, `PatchlabTicketing` polls the database every few seconds rather than receiving a push notification when a new ticket is written. That was a deliberate choice, not a placeholder, live push (SignalR) was considered and declined for now.

Keeping the two apps separate means this bot stays narrowly scoped to "receive message, reply", and doesn't need redeploying just because dashboard UI changes.

`PatchlabNgrokSync` is a small companion service that keeps a tunnelling provider's registered public URL in sync with Meta's Callback URL setting whenever the tunnel's address changes across restarts.

## Known gotchas

- `.NET 10`'s built in OpenAPI support currently fails to compile against `Microsoft.OpenApi` 3.x (`CS0200`). Pin to `Microsoft.OpenApi 2.3.9`, or drop Swagger entirely, it isn't adding value on a webhook only service.
- Meta's webhook GET verification (`hub.challenge`) must be echoed back as plain text, not wrapped in JSON.
- Meta enforces a 24 hour customer service window: free form text replies are only allowed within 24 hours of the customer's last message. Outside that window, business initiated messages require pre approved templates. This mainly affects manual dashboard testing (messaging yourself first), it shouldn't affect the real bot flow, since customers always message in first.
- Meta's "service conversations" (replying to inbound messages) are currently free, but Meta has a pricing change coming October 1, 2026 making service/utility messages inside the customer service window chargeable.
- Unverified Cloud API apps are capped at 250 unique customers messaged per rolling 24 hours. Business Verification removes this cap, not yet done, deferred until/unless the limit is hit.
- WhatsApp interactive button messages cap out at 3 buttons, 20 characters each; interactive lists cap at 10 rows, 24 characters per title, 72 per description. Sending more/longer than that will be rejected by the Graph API.
- PowerShell's `curl` alias mangles curl style flags, use `-Headers @{...}` and `-UseBasicParsing` instead.
- A process manager showing "Running" does not confirm health, verify with a live request or log check.
- A schema change made by hand directly against a database (rather than via `Add-Migration`) will desync EF's migration history from reality — the next real migration applied there may then conflict with what already exists. Keep all schema changes going through migrations once adopted, even "quick" ones.
- Any secret pasted into a chat or AI session should be treated as compromised and rotated.

## Roadmap

- [ ] **"Other clients" branch**, routing non LSF senders into existing client with an issue versus new client looking for something flows. Should be designed with the eventual manual takeover flow (now in `PatchlabTicketing`) in mind.
- [ ] Actual server hosting environment for this bot (currently still on the original dev-adjacent machine) — migrate following the generic steps above once the target is chosen.
- [ ] CI/CD deploy ordering flaw, a failed `dotnet publish` leaves the service stopped indefinitely with no alert. Applies to this project, `PatchlabNgrokSync`, and `PatchlabTicketing`.
- [ ] Replace the in memory `ConversationStore` with persistent storage, it is currently wiped on every service restart.
- [ ] Read and log Meta's error response body on send failures instead of letting `EnsureSuccessStatusCode()` throw blind.
- [ ] Replace manual `JsonElement` parsing of Meta's webhook payload with strongly typed DTOs and `TryGetProperty` guards throughout.
- [ ] Bump Graph API calls from `v21.0` to `v26.0` before Meta removes `v21.0` support.
- [ ] Switch this service's process-manager account off `LocalSystem`/equivalent to a dedicated least privilege account. Should happen before going live, does not affect day to day development.
- [ ] Wire `Tickets.GetLatestStatusCommentAsync` to wherever ticket status comments actually live, rather than the current placeholder reflecting the `Status` column directly.
- [ ] Remove the dead `MetaWhatsAppOptions.SectionName` code, cosmetic, low priority.
- [ ] Delete the stray `PatchlabTwilioBot.csproj.Backup.tmp` file at the repo root.

## Security notes for anyone cloning this

- **Never commit real credentials.** `config.json` in this repo should never contain real values, only `config-example.json` should be committed, with placeholders.
- Use environment variables, Azure Key Vault, or an equivalent secret store in production rather than a plain `config.json` on disk, if that becomes a concern.
- If `PatchlabWhatsAppBot.csproj.Backup.tmp` or similar stray files exist in your working copy, don't commit them, clean up before pushing.
- Database connection strings, access tokens, and staff notification numbers are all equally sensitive — treat all four `config.json` values as credentials, not just the ones that look like secrets.