# Escaler

**Know who knows.**

Escaler finds the colleague who can help you, and makes sure they take the task.

You describe a problem in plain words. An AI matches you with the most relevant colleagues, based on their skills and on how much they already have on their plate. The best match gets the request with two buttons: **I'll take it** or **Not me, pass it on**. A pass sends it straight to the next best person, so no question gets stuck or lost.

Escaler runs as a web app, with an optional Slack add-on so people can ask from their chat.

---

## Why

- Teams spend about **25%** of their time searching for answers ([Atlassian, State of Teams 2025](https://www.atlassian.com/blog/state-of-teams-2025)).
- **62%** of people spend too much time searching for information, communicating and coordinating ([Microsoft, Work Trend Index 2023](https://blogs.microsoft.com/blog/2023/05/09/introducing-the-microsoft-365-copilot-early-access-program-and-2023-microsoft-work-trend-index/)).
- In most companies **3–5%** of employees give **20–35%** of all the valuable help (Cross, Rebele & Grant, "Collaborative Overload", *Harvard Business Review*, 2016).

Search tools tell you who *might* know. Escaler makes sure someone actually helps, and spreads the work so the same few experts don't burn out.

## Features

| Area | What it does |
|---|---|
| **AI routing** | Ranks colleagues for a problem by skills first, then by current workload. Asks one follow-up question when a request is too vague. |
| **Accept or pass** | The request moves down a ranked list of up to three people until someone takes it. Accepting opens a group DM in Slack, or shows the request as in progress on the web. |
| **Expert directory** | Profiles with title, team, skills and availability. Search, filters and sorting. Skills can be suggested by AI or filled from a LinkedIn PDF that the person uploads themselves. |
| **Handoffs board** | Every request with its status, the AI's reason for each match, and simple stats like the first-try rate. |
| **Manager priorities** | Managers post priority tasks for chosen teams, with a level and due date, plus "Got it" read tracking and optional Slack DMs. |
| **Outside help** | When nobody inside fits, Escaler suggests the company's external partners, then can search the web for specialists. A person always sends any message. |
| **Slack add-on** | DM the bot or @mention it in a channel. One-click import of the whole workspace as draft profiles, and invite DMs so people can check their draft. |
| **Demo company** | A realistic example organisation (26 people, two weeks of requests, priorities, partners) that admins can remove in one click, with undo. |

## Tech stack

| Layer | Technology |
|---|---|
| Backend | C# 14 / .NET 10, on the Ikon AI app platform |
| UI | Server-driven reactive UI (Ikon Parallax): components are declared in C# and streamed to the browser over WebSocket; styling uses Crosswind, Tailwind-style utility classes |
| Frontend shell | React 19 + Vite + TypeScript (`frontend-node/`), which renders the streamed UI and handles sign-in |
| AI | Claude (Sonnet for routing and LinkedIn parsing, Haiku for skill suggestions and search queries) through the Ikon Emergence API, with typed structured output |
| Web search | Ikon WebSearcher with automatic fallback between search providers |
| Integrations | Slack Events API, Interactivity (buttons) and Web API |
| State | Ikon persistent reactive collections (experts, handoffs, priorities) |
| Auth | Ikon sign-in (Google, Microsoft, email, passkey). Sign-in is required; there is no guest access |

## Architecture

```
            Web app (React shell)            Slack
                    │                          │
          WebSocket │ reactive UI   Events API │ + Interactivity (signed HTTP)
                    ▼                          ▼
        ┌───────────────────────────────────────────────┐
        │  EscalerApp (C# / .NET 10, one shared instance)│
        │                                               │
        │   Views ── Ask · People · Handoffs ·          │
        │            Priorities · Slack setup · Profile │
        │                                               │
        │   Routing ── PlanRouteAsync ──► Claude         │
        │      │        (skills + workload → ranked      │
        │      │         matches, reasons, urgency)      │
        │      ▼                                         │
        │   Handoff lifecycle                            │
        │   Waiting → Accepted → Done                    │
        │      └─ Pass → next candidate / Unassigned     │
        │                                               │
        │   Persistent state: experts · handoffs ·       │
        │                     priorities                 │
        └───────────────────────────────────────────────┘
```

| File | Responsibility |
|---|---|
| `app/Escaler/EscalerApp.cs` | Entry point, theme, layout, navigation, sign-in handling |
| `app/Escaler/Routing.cs` | AI matching prompt, handoff lifecycle (accept, pass, done, withdraw) |
| `app/Escaler/Slack.cs` | Slack webhooks, signature checks, bot messages, account linking |
| `app/Escaler/SlackImport.cs` | Workspace import, skill guessing, invite DMs, admin rules |
| `app/Escaler/Outside.cs` | External partners and the web search for specialists |
| `app/Escaler/Priorities.cs` | Manager priority tasks and their audiences |
| `app/Escaler/*View.cs` | The Ask, People, Handoffs, Profile and Slack setup screens |
| `app/Escaler/DemoData.cs` | The removable example company |
| `app/Escaler/Models.cs` | Records for experts, handoffs, priorities and AI results |

## Security and privacy

- **Signed webhooks.** Every Slack request is checked with HMAC-SHA256 using the signing secret and a constant-time comparison. Requests older than 5 minutes are rejected, and retries are de-duplicated by event id.
- **No identity takeover.** Slack accounts are linked only through the *verified* sign-in email, and one Slack account can belong to only one profile.
- **Need-to-know visibility.** A request is only visible to the person who asked and the person it was sent to. Admins can see everything.
- **Prompt-injection hardening.** Profile text and problems are fenced off as data in the AI prompt, flattened to single lines, and the model is told to ignore any instructions in them. Model output is length-capped, and only roster ids are accepted.
- **Output escaping.** All user and AI text is escaped before it goes into Slack messages, so nobody can inject pings or disguised links.
- **Input limits.** Name, title, skills, problems and priorities have length caps, checked again on the server.
- **Privacy by design.** Escaler never reads Slack channels, email or files, only what people write to it. Secrets (Slack tokens, admin list) are stored as platform secrets, never in the code.
- **Safe outbound calls.** Slack callbacks go only to `hooks.slack.com`, and web-search links are limited to URLs the search itself returned.

## Running it

Escaler runs on the Ikon platform, so you need an Ikon account and the `ikon` CLI.

```bash
ikon run       # start locally (server + frontend)
ikon deploy    # deploy to the cloud
```

Optional settings, stored as app secrets:

| Secret | Purpose |
|---|---|
| `SLACK_BOT_TOKEN` | Bot User OAuth Token (`xoxb-…`) for the Slack add-on |
| `SLACK_SIGNING_SECRET` | Verifies that webhook requests really come from Slack |
| `ESCALER_ADMIN_EMAILS` | Comma-separated emails that get admin rights |

```bash
ikon secret set SLACK_BOT_TOKEN
```

Setting up Slack: the app's **Slack** page shows a ready-to-paste Slack app manifest with the webhook URLs filled in, plus step-by-step instructions.

## Status

A working prototype, pitched to its first companies. Planned next:
- **Storage:** move the data to Postgres tables, so every change is saved the moment it happens.
- **Insights:** a manager dashboard with weekly charts.

## Author

Marlon Pehrsson
