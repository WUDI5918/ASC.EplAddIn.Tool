 # Repository Guidelines
 
 ## Project Structure & Module Organization
 
 This is an EPLAN add-in for rule-driven automatic schematic generation, built with C# and WPF (.NET 4.8.1, MVVM pattern). Source files live at the project root — there is no `src/` directory.
 
 - **Core domain**: `ConfiguratorModel.cs`, `ConfiguratorService.cs`, `AddInMenuModel.cs`
 - **UI (WPF)**: `ConfiguratorWindow.xaml` / `.cs`, `GenerationLogWindow.xaml` / `.cs`, `LibrarySettingsDialog.xaml` / `.cs`, `TypicalVariablesInputDialog.xaml` / `.cs`
 - **Configuration data**: `configurator_data.json` (macro-typical rules), `breaker_catalog.csv` (component lookup)
 - **Project file**: `AutoSchematicConfigurator.EplAddIn.Tool.csproj`
 - **Solution file**: `AutoSchematicConfigurator.EplAddIn.Tool.slnx`
 - **Specifications & agent configs**: `specs/` (spec, plan, tasks), `.github/agents/` (spec-kit agents), `.github/prompts/`, `.specify/`
 - **Build output**: `bin/Debug/`, `bin/x64/Debug/`, `bin/Release/`
 
 ## Build, Test, and Development Commands
 
 ```powershell
 # Restore NuGet packages
 dotnet restore
 
 # Build the project (Debug, AnyCPU)
 dotnet build
 
 # Build for x64
 dotnet build -p:Platform=x64
 
 # Build in Release mode
 dotnet build -c Release
 
 # Run tests (when test project exists)
 dotnet test
 ```
 
 The add-in must be registered with EPLAN to run. Build produces a class library (`.dll`) that loads into the EPLAN Platform 2026.0.3 process.
 
 ## Coding Style & Naming Conventions
 
 - **Language**: C# with WPF, targeting .NET 4.8.1
 - **Naming**: PascalCase for public members, types, methods, and properties. `_camelCase` for private fields. Interfaces prefixed with `I`.
 - **Indentation**: 4 spaces per level (no tabs).
 - **Expression engine**: Rule formulas use `NCalc-Edge` syntax — logical (`&&`, `==`), ternary, string, math, and custom lookup functions (`Lookup_Breaker`).
 - **Formatting**: Configured in `.editorconfig` (project root). Use `dotnet format` to auto-fix style issues.
 
 ## Testing Guidelines
 
 A dedicated test project has not been created yet. When adding one:
 
 - Place it in a `tests/` directory alongside the solution.
 - Use xUnit as the framework (aligns with typical .NET ecosystem).
 - Name tests with the pattern `MethodName_Should_ExpectedBehavior` (e.g. `EvaluateCondition_Should_SkipInactiveTypical`).
 - Run tests with `dotnet test` from the solution root.
 - Aim for unit tests on `ConfiguratorService` logic and integration tests on EPLAN API wrappers (using test stubs).
 
 ## Commit & Pull Request Guidelines
 
 This project uses **Spec-driven development (Spec Kit)**. No commits exist yet, so conventions are defined for forward use:
 
 - **Commit messages**: Prefix with the conventional type (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`). Keep the first line under 72 characters. Reference the related spec issue (e.g. `feat: add breaker lookup table support`).
 - **Branch naming**: Use descriptive short names: `feat/rules-engine`, `fix/coordinate-offset`.
 - **Pull requests**: Every PR must link to a spec (`docs/spec.md` or a GitHub issue), include a summary of changes, and note any configuration changes (`configurator_data.json` schema updates, new CSV catalogs). Attach EPLAN screenshots for UI or generation-affecting changes.
 
 ## Agent Integration
 
 AI coding agents work through `.github/agents/` with Spec Kit workflows. The `.github/copilot-instructions.md` file points agents to the current plan for context. Agents should read `specs/spec.md` and `specs/plan.md` before making changes. Configuration files such as `.specify/extensions.yml` define auto-execution hooks for Git operations before specification, planning, and implementation phases.
