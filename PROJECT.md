# Project: EPLAN Schematic Generator Add-In Parity

## Architecture
This is an EPLAN add-in designed for automatic schematic generation using WPF and C# (.NET 4.8.1).
It parses a typical database (`configurator_data.json`) and generates EPLAN pages based on rules.

- **ConfiguratorModel.cs / LibrarySettings.cs**: Data models for configurations and settings.
- **ConfiguratorService.cs**: Coordinates data loading, evaluation, placeholder resolution, and page generation.
- **ConfiguratorService.Eplan.cs**: Wrapper for EPLAN API operations (creating pages, placing macros).
- **ConfiguratorService.Placeholders.cs**: Handles placeholder name resolution.
- **ConfiguratorService.Evaluation.cs**: Evaluates rules and formulas.
- **ConfiguratorService.Generator.cs**: Drives the page generation workflow.

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Investigation | Analyze codebase structure, EPLAN dependencies, and exact requirements | None | DONE |
| 2 | E2E Test Suite Design | Establish a test runner and write the required Tiers 1-4 tests (49+ cases) | M1 | DONE |
| 3 | R2: Newtonsoft.Json List Duplication Fix | Fix LibrarySettings selected languages and available structure identifiers duplication | M2 | DONE |
| 4 | R3: Placeholder Name Matching | Support `.` and `:` as separators for placeholders | M2 | DONE |
| 5 | R1: Page Structure designation support | Support `&` and `#` in page designations, format page names correctly | M2 | DONE |
| 6 | R4: Formula Stability & Validation | Implement validation on mandatory variables, exception logging, and WPF update | M2 | DONE |
| 7 | Verification & Review | Run full test suite, verify build and formatting, pass Forensic Audit | M3, M4, M5, M6 | DONE |
| 8 | R1: Optimize Macro Selector First Load Speed | EPLAN page query with false, async directory scanning, non-blocking UI | None | DONE |
| 9 | R2: Load Placeholder Variables for Page Macros | Load page macro placeholders, valid page target in Insert, index-based mapping, tree view expansion | None | DONE |
| 10 | R3: Resolve UI Shaking when Resizing Navigator | Update main editor column in XAML, set MinWidth on child Grid | None | DONE |
| 11 | R4: Remove "Import from Disk" Feature | Remove import button, context menu item, event handlers, and clean up code | None | DONE |
| 12 | Final Verification & Audit | Ensure all 63 tests pass, check code formatting, and pass Forensic Audit | M8, M9, M10, M11 | DONE (Conv: 648f1b1c-114e-4fba-b731-4572ac665a10) |

## Interface Contracts
- **ConfiguratorService.Eplan ↔ EPLAN API**: Uses Page properties `DESIGNATION_DOCTYPE` and `DESIGNATION_USERDEFINED`. Uses `finder.GetAll<Page>(false)` for fast lookup. Passes active page to `Insert.PageMacro`.
- **ConfiguratorService.Placeholders**: Resolves placeholder matching for `Placeholder.Var` and `Placeholder:Var`. Handles page macro placeholders mapped by index.
- **Newtonsoft.Json Settings**: `ObjectCreationHandling.Replace` for deserializing lists.
- **WPF UI Layout**: Center workspace column has no `MinWidth`, child container handles it.

## Code Layout
- Root directory: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\`
- Sub-agent folders: `.agents/`
- Tests: `tests/` directory
- Build outputs: `bin/Debug/`
