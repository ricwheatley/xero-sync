# XeroSync

XeroSync is a **.NET 8** solution that keeps a Xero tenant and a SQL Server (or Azure SQL) warehouse in sync for BI and forecasting work.

<table>
<tr><td width="33%">
<b>Worker</b><br/>
Console app that performs the heavy lifting – it refreshes its access token automatically, downloads day‑to‑day endpoints and monthly financial reports, and writes raw JSON + processed tables.
</td><td width="33%">
<b>Auth Listener</b><br/>
Single‑run helper that completes the OAuth authorisation flow and writes an encrypted <code>token.dat</code> so the Worker can run head‑less thereafter.
</td><td width="33%">
<b>Desktop Launcher</b><br/>
A tiny WPF front‑end for people who prefer a window over a terminal.  Lets you choose what to run, pick the FY range, select a tenant GUID, and watch a live log.
</td></tr></table>

---

## Repository layout

```
XeroSync.sln                – solution file (Worker, Auth, Desktop)
│
├── XeroSync.Worker/        – core worker service
│   └── Core/               – runners, orchestrator, helpers
│   └── Services/           – TokenStore, XeroReportFetcher, …
│
├── XeroSync.Auth/          – one‑time OAuth bootstrap
│
├── XeroSync.Desktop/       – WPF launcher (manual runs)
│     └── Assets/           – logo & icon
│
└── config/
      ├── client.template.json  ←  copy to client.json, add secrets
      ├── uiSettings.json       ←  persisted Desktop UI choices
      └── token.dat             ←  encrypted after first auth (ignored)
```

---

## Desktop launcher (new!)

`XeroSync.Desktop` is meant for book‑keepers and accountants who don’t keep a terminal open.

| Control | Purpose |
|---------|---------|
| **Run mode** dropdown | `SupportData`, `Reports`, or `Both` (default) |
| **Start / End FY dates** | Two `DatePicker`s; defaults to current FY. |
| **Tenant GUID** | Populated from <code>uiSettings.json</code> after your first successful run. |
| **Run** button | Starts the sync and streams log lines into the **280‑px‑high** console area. |
| **Close** button | Exits the app (enabled as soon as a run finishes). |

The main window is **520 px high** to accommodate the larger log pane.

All settings (last run mode, dates, tenant) are persisted to `config/uiSettings.json` so the next launch is pre‑filled.

> **Tip:** You can still pass `--support` or `--reports` on the command line to skip the UI entirely.

---

## Quick‑start (local machine)

```bash
# 1  Clone
> git clone https://github.com/ricwheatley/xero-sync.git
> cd xero-sync

# 2  Create secrets file
> cp config/client.template.json config/client.json
#   – edit ClientId / ClientSecret from Xero Dev Portal

# 3  Bootstrap once
> dotnet run --project XeroSync.Auth    # opens browser, writes token.dat

# 4a  Run head‑less (support + reports for FY‑24/25)
> setx XERO_SQL_CONN "Server=.;Database=XeroSync;Trusted_Connection=True;Encrypt=False;"
> dotnet run --project XeroSync.Worker -- both 2024-04-01 2025-03-31

# 4b  …or launch the Desktop app
> dotnet run --project XeroSync.Desktop
```

> Subsequent runs need only step 4 – the token is refreshed automatically.

---

## Configuration reference

| File / var | When used | Notes |
|------------|-----------|-------|
| **config/client.json** | always | `ClientId`, `ClientSecret`, optional `SqlConn`, optional bootstrap `RefreshToken` |
| **config/uiSettings.json** | by Desktop app | remembers last run mode, dates, tenant GUID |
| **XERO_SQL_CONN** env‑var | worker runs | overrides `SqlConn` in JSON |
| **token.dat** | after first auth | machine‑specific, encrypted with DPAPI on Windows |

---

## CI/CD cheatsheet

1. **Secrets** – store the four env‑vars (`XERO_*` + `SQL`) in your pipeline secret store.
2. **Build** – compile Worker & Auth projects:

   ```yaml
   - task: DotNetCoreCLI@2
     inputs:
       command: build
       projects: |
         **/XeroSync.Worker.csproj
         **/XeroSync.Auth.csproj
   ```
3. **Bootstrap** – run AuthListener once on a self‑hosted agent to produce `token.dat`.
4. **Schedule** – cron a daily `dotnet XeroSync.Worker.dll -- both` job.

---

## Security notes

* `token.dat` is DPAPI‑encrypted on Windows; plaintext fallback on Linux (swap in a cloud KMS if needed).
* `.gitignore` excludes `token.dat`, `client.json`, and other secrets.

---

## Licence

MIT – see `LICENSE`.

---



