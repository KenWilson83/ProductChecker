# ProductChecker

A C# / ASP.NET Core web app that accepts multiple product page URLs and reports whether the store publishes availability for online purchase.

## Run

Install the .NET 10 SDK (10.0.403, pinned in `global.json`). From the repository root:

```sh
dotnet restore src/ProductChecker/ProductChecker.csproj
dotnet build src/ProductChecker/ProductChecker.csproj --no-restore
dotnet run --project tests/ProductChecker.Tests
dotnet run --project src/ProductChecker --no-build --urls http://127.0.0.1:5080
```

Open the app on port 5080 in a local development browser. Enter one HTTPS product URL per line (up to 10 unique URLs), then select Check availability. Each URL gets its own result in input order; failed or invalid URLs do not stop the other checks. Blank lines and exact duplicates are ignored. The app runs up to two checks concurrently and keeps the existing ten-checks-per-minute limit. `GET /health` returns `{"status":"ok"}`. Submit `POST /api/check` with JSON `{"url":"https://store.example/product"}`. Results contain `status`, `message`, and `evidence`. The dependency-free test executable exits nonzero on failure; use `dotnet run`, not `dotnet test`, to execute it.

Run the browser workflow tests with Node.js 20+ using `node --test tests/web/checks.test.mjs`.

## Scope

- Reads explicit Schema.org Product offers in JSON-LD embedded in HTML.
- Reports available, unavailable, or unknown. Mixed variants, preorder/backorder, missing data, and fetch failures are unknown. Availability is an observation, not a guarantee of checkout success.
- Does not search by product name, execute JavaScript, bypass store access restrictions, sign in, or make purchases. Store-specific API integrations can be added later.
- No database, API keys, or external NuGet packages are required.
- Fetching uses public HTTPS destinations on port 443, validated DNS addresses on each connection, at most three redirects, bounded response sizes and deadlines, and a limit of ten checks per client IP per minute. Deploy behind appropriate authentication and infrastructure rate limits before exposing it broadly.
- Direct connections enforce public DNS address checks. When an outbound proxy is configured, an administrator must authorize exact store hostnames in `PRODUCTCHECKER_STORE_HOSTS` (comma-separated, no wildcards). Every redirect must also be authorized. Use only a trusted proxy with appropriate destination restrictions, because it resolves the destination itself. Cloud environment domain restrictions must also permit those store domains. Without this configuration, proxied checks return unknown.

## Cloud development

The prepared SDK lives at `/workspace/.tools/dotnet`. Set `PATH=/workspace/.tools/dotnet:$PATH`, `DOTNET_CLI_HOME=/workspace/.cache/dotnet`, and `DOTNET_CLI_TELEMETRY_OPTOUT=1` before using the commands above. Each cloud task is isolated; use the existing checkout without creating a worktree unless requested.
