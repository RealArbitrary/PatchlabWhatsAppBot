# PatchlabTwilioBot

A WhatsApp auto-response bot for [Patchlab](https://github.com/) (IT support & web dev), built on ASP.NET Core and Meta's WhatsApp Cloud API.

> **Note on the name:** the project started life as a Twilio Sandbox proof of concept (hence `PatchlabTwilioBot`), then migrated fully to Meta's Cloud API to avoid Twilio's per-message markup. Twilio is no longer used anywhere in the codebase — the name just stuck.

## What it does

Patchlab's WhatsApp number receives messages from two kinds of senders:

- **LSF Personeel** — staff at a school (a contact of the developer's) who message in to log an issue and get a ticket number. *(implemented)*
- **Everyone else** — existing clients with an issue, or new clients looking for something. *(not yet implemented — see Roadmap)*

The bot runs a simple state-machine conversation per phone number and replies automatically. A human can also take over a conversation manually from the same number/thread — since the number is no longer usable in the normal WhatsApp Business consumer app once it's on the Cloud API.

## Current flow (LSF Personeel)

```
"hi"              → "Hi! What's your issue?"
<issue text>       → "Thanks — ticket TCKT-<timestamp> created. We'll get back to you."
```

## Stack

- ASP.NET Core Web API, .NET 10
- Meta WhatsApp Cloud API (Graph API `v21.0`)
- In-memory `ConcurrentDictionary`-based conversation store (POC-grade — see Roadmap)
- ngrok for local webhook tunneling during development

## Project structure

```
PatchlabTwilioBot/
├── Controllers/
│   └── WhatsAppWebhookController.cs   # GET verification handshake + POST message handling
├── Conversations/
│   ├── ConversationState.cs           # enum: New, AwaitingIssue, AwaitingClientType (unused), Completed
│   ├── ConversationSession.cs         # per-number session: State, IssueText, TicketNumber
│   └── ConversationStore.cs           # in-memory store keyed by phone number
├── WhatsApp/
│   ├── MetaWhatsAppOptions.cs         # bound config: PhoneNumberId, AccessToken, VerifyToken
│   ├── IWhatsAppSender.cs
│   └── MetaWhatsAppSender.cs          # sends replies via graph.facebook.com
└── Program.cs
```

`Conversations/` is transport-agnostic — it has no idea whether messages came in via Twilio or Meta. `WhatsApp/` is where the Meta Cloud API–specific plumbing lives. This split is what made the Twilio → Meta migration a controller-and-transport-layer rewrite only, not a rewrite of the actual conversation logic.

## Setup

1. **Create a Meta app** (Business type) in [Meta for Developers](https://developers.facebook.com/), add the WhatsApp product.
2. **Register your WhatsApp number** with the Cloud API.
   - ⚠️ If the number is already active in the normal WhatsApp Business consumer app, you'll need to disconnect/migrate it first — this is a one-way move; the number can no longer be used in the consumer app afterwards. Export any chat history you want to keep before doing this.
3. **Generate a permanent access token.** The token shown on the API Setup / quickstart page is often labeled "permanent" but may actually be a 24-hour temporary token — verify with [Meta's Access Token Debugger](https://developers.facebook.com/tools/debug/accesstoken/) (`Expires: Never` + `Data Access Expires: Never` = genuinely permanent). For a real permanent token, generate one via **Business Settings → System Users**.
4. **Set your local secrets** (do not commit these — see below):
   ```
   dotnet user-secrets init
   dotnet user-secrets set "Meta:PhoneNumberId" "<your phone number id>"
   dotnet user-secrets set "Meta:AccessToken" "<your permanent system user token>"
   dotnet user-secrets set "Meta:VerifyToken" "<any string you invent>"
   dotnet user-secrets list   # to confirm
   ```
5. **Run the app**, then tunnel it with ngrok — **must target the HTTPS port explicitly**:
   ```
   ngrok http https://localhost:7055
   ```
   (`ngrok http 7055` alone won't speak TLS to the dev-cert HTTPS endpoint and will silently hang.)
6. **In Meta's dashboard**, under WhatsApp → Configuration, set:
   - Callback URL: `https://<your-ngrok-subdomain>.ngrok-free.app/webhook/whatsapp`
   - Verify token: the same string you set as `Meta:VerifyToken`
   - Subscribe to the `messages` field
7. Click **Verify and save** — this triggers Meta's one-time GET handshake against your running app.

## Known gotchas

- `.NET 10`'s built-in OpenAPI support currently fails to compile against `Microsoft.OpenApi` 3.x (`CS0200`). Pin to `Microsoft.OpenApi 2.3.9`, or drop Swagger entirely — it isn't adding value on a webhook-only service.
- `dotnet user-secrets set` fails with `Could not find the global property 'UserSecretsId'` unless `dotnet user-secrets init` has been run first in the project.
- Meta's webhook GET verification (`hub.challenge`) must be echoed back as **plain text**, not wrapped in JSON.
- Meta enforces a 24-hour customer-service window: free-form text replies are only allowed within 24 hours of the customer's last message. Outside that window, business-initiated messages require pre-approved templates. This mainly bites *manual dashboard testing* (trying to message yourself first) — it shouldn't affect the real bot flow, since customers always message in first.
- Meta's "service conversations" (replying to inbound messages) are currently free, but Meta has a pricing change coming **October 1, 2026** making service/utility messages inside the customer-service window chargeable.
- Unverified Cloud API apps are capped at 250 unique customers messaged per rolling 24 hours. Business Verification removes this cap — not yet done, deferred until/unless the limit is hit.

## Roadmap

- [ ] **Manual takeover tool** — a small internal page to view an in-progress conversation and send a reply through the same backend/number, since the number is no longer reachable via the normal consumer app.
- [ ] **"Other clients" branch** — route non-LSF senders into existing-client-with-an-issue vs. new-client-looking-for-something flows.
- [ ] Replace the in-memory `ConversationStore` with persistent storage (survives app restarts, scales beyond a single instance).
- [ ] Read and log Meta's error response body on send failures instead of `EnsureSuccessStatusCode()` throwing blind.
- [ ] Replace manual `JsonElement` parsing of Meta's webhook payload with strongly-typed DTOs and `TryGetProperty` guards throughout.
- [ ] LSF ticketing system integration (currently just captures issue text + generates a ticket number, no external system).

## Security notes for anyone cloning this

- **Never commit real credentials.** `appsettings.json` in this repo should not contain real `Meta:*` values — use `dotnet user-secrets` locally, and environment variables / Azure Key Vault (or equivalent) in production.
- If `PatchlabTwilioBot.csproj.Backup.tmp` or similar stray files exist in your working copy, don't commit them — clean up before pushing.
