# MiniCRM

A small ASP.NET Core CRM demo for following clients, sales opportunities and follow-up tasks. Built with C# and server-rendered HTML, without a frontend framework or database server.

## Features

- Client search and Client 360: contact details, notes, linked deals and tasks
- Six-stage deal pipeline, reassignment and activity history
- Follow-ups with priorities, due dates, completion and calculated overdue status
- Dashboard metrics, filtered reports and saved views
- Paginated PDF and Excel 2003 XML exports, including Unicode text
- Demo users, configurable module permissions, account blocking and administrative audit log
- Local JSON persistence with serialized changes and atomic file replacement

## Run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then:

```sh
git clone https://github.com/dima1sik/mini-crm.git
cd mini-crm
dotnet restore --locked-mode
dotnet run --urls http://localhost:5000
```

Open `http://localhost:5000`. A fresh store is populated with fictional data dated relative to the current UTC date. No API keys or external services are needed.

Data is saved to ignored `App_Data/crm-data.json`. To use a separate file:

```sh
dotnet run --urls http://localhost:5000 -- --Crm:DataPath=/absolute/path/crm-data.json
```

**Reset demo** replaces all local data. Back up the JSON file before resetting. Invalid existing data stops startup with an error; it is never silently replaced.

## Test

```sh
dotnet test tests/MiniCRM.Tests/MiniCRM.Tests.csproj
```

The regression suite covers HTTP workflows, permissions, blocked users, CSRF, validation, concurrent changes, persistence failures and report exports. Each test uses its own temporary store.

## Structure

`Program.cs` configures the app. `Web/` contains endpoints and HTML rendering, `Services/` contains workflow rules and demo access checks, `Data/` manages JSON snapshots and sample data, `Models/` defines entities, and `Reporting/` builds reports and exports. PDF generation uses [PDFsharp](https://docs.pdfsharp.net/); the bundled DejaVu Sans font has its redistribution license in `Reporting/Fonts/LICENSE.txt`.

## Scope

This is a local learning and portfolio project. The user selector deliberately switches demo identities; **there is no password-based authentication**, so this should not be deployed as a public CRM. Module checks and CSRF protection run on the server, but do not turn the selector into a login system.

JSON storage supports one application process, without database transactions across multiple instances. Owners are a fixed fictional team. Reports use deal creation dates and current stages rather than historical close-date accounting. Spreadsheet export is Excel-compatible XML, not a native binary XLS workbook; the `/reports/export/xls` route is retained for compatibility.

[Dashboard screenshot](screenshots/dashboard.png) · [Other screenshots](screenshots/README.md). These show the original sample dataset; dates and labels may differ from the current app.
