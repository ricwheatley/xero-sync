# XeroSync

**XeroSync** is a .NET 8 worker‑service that pulls data from the Xero Accounting API and stores it in a SQL Server database for reporting, BI, and downstream processing. It is designed to run unattended on Windows or Linux, refreshing its access token automatically and processing both day‑to‑day endpoints and monthly financial reports.

The repository also includes a tiny **AuthListener** helper that performs the once‑off OAuth 2.0 authorisation flow, writing an encrypted `token.dat` so the worker can operate head‑less thereafter, plus a WPF **Desktop Launcher** for manual starts.

---

## Repository layout

```
XeroSync.sln                Solution file with Worker, Auth, Desktop projects
│
├── XeroSync.Worker/        Core worker service (console)
│   └── Core/               – runners, orchestrator, helpers
│   └── Services/           – TokenStore, XeroReportFetcher, …
│
├── XeroSync.Auth/          One‑time bootstrap tool (interactive OAuth)
│   └── AuthListener.cs
│
├── XeroSync.Desktop/       Optional WPF launcher (not the refactor focus)
│
└── config/
    ├── client.template.json  ← sample secrets file (copy to client.json)
    └── token.dat             ← generated after first authorisation (ignored)
```

---

## Prerequisites

| Tool           | Version | Notes |
|----------------|---------|-------|
| [.NET SDK]     | **8.0** | `dotnet --version` should report ≥ 8.0.100 |
| SQL Server     | 2017+   | Local or Azure SQL DB |
| Git            | any     | Clone / commit |
| Browser        | any     | For the OAuth consent screen |

[.NET SDK]: https://dotnet.microsoft.com/download

---

## Quick‑start (local machine)

```bash
# 1  Clone
> git clone https://github.com/ricwheatley/xero-sync.git
> cd xero-sync

# 2  Create secrets file
> cp config/client.template.json config/client.json
#   – edit ClientId / ClientSecret (from Xero Dev portal)
#   – leave RefreshToken blank for now

# 3  Bootstrap tokens once
> dotnet run --project XeroSync.Auth           # opens browser, writes token.dat

# 4  Run the worker (support + reports for FY24‑25)
> setx XERO_SQL_CONN "Server=.;Database=XeroSync;Trusted_Connection=True;Encrypt=False;"
> dotnet run --project XeroSync.Worker -- both 2024-04-01 2025-03-31
```

The first run will:
1. Reuse the `token.dat` written by **AuthListener**
2. Discover the tenant, sync eight standard endpoints, then loop through monthly financial reports.

Subsequent runs need only step 4 – the token is refreshed automatically.

---

## Configuration reference

### `config/client.json`

| Key              | Required | Description |
|------------------|----------|-------------|
| `ClientId` / `client_id`         | ✓ | Xero app Client ID |
| `ClientSecret` / `client_secret` | ✓ | Xero app Client Secret |
| `RefreshToken` / `refresh_token` | (once) | Bootstrap token written manually or by AuthListener. Safe to delete after `token.dat` exists. |
| `SqlConn` / `sql_conn`           | optional | Falls back if `XERO_SQL_CONN` env‑var not set |

### Environment variables

| Variable          | When needed | Example value |
|-------------------|-------------|---------------|
| `XERO_SQL_CONN`   | always      | `Server=.;Database=XeroSync;Trusted_Connection=True;` |
| `XERO_CLIENT_ID`  | (CI)        | overrides `client.json` |
| `XERO_CLIENT_SECRET` | (CI)     | overrides `client.json` |
| `XERO_REFRESH_TOKEN` | bootstrap | alternative to putting it in JSON |

### Command‑line

```bash
# Run just support‑data endpoints
> dotnet run --project XeroSync.Worker -- support

# Run only financial reports for FY‑23
> dotnet run --project XeroSync.Worker -- reports 2023-04-01 2024-03-31
```

---

## CI/CD guidance

1. **Secret variables** – store the four env‑vars above in your pipeline secret store.
2. **Build** – compile Worker & Auth projects:
   ```yaml
   - task: DotNetCoreCLI@2
     inputs:
       command: build
       projects: |
         **/XeroSync.Worker.csproj
         **/XeroSync.Auth.csproj
   ```
3. **Bootstrap step** (self‑hosted agent only): run AuthListener once if `token.dat` isn’t cached on the runner.
4. **Scheduled run** – cron a daily `dotnet XeroSync.Worker.dll -- both` task.

---

## Security notes

* `token.dat` is encrypted with Windows DPAPI (CurrentUser scope). On non‑Windows hosts it falls back to plain‑text (you can swap DPAPI for Azure Key Vault or GCP KMS).
* `.gitignore` excludes both `token.dat` and `client.json`. Never commit real secrets.

---

## Contributing

1. **Fork** the repo ↗️
2. Create a feature branch: `git checkout -b feature/your‑idea`
3. Run `dotnet format` before pushing.
4. Open a Pull Request – small, focused changes are easier to review.

---

## Licence

MIT – see `LICENSE` file for details.

