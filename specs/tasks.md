# AutoSchematicConfigurator Task Checklist

- [x] Project Setup & Solution Fixes
  - [x] Fix solution mapping in `AutoSchematicConfigurator.EplAddIn.Tool.slnx`
  - [x] Update assembly metadata and add NuGet PackageReferences in `AutoSchematicConfigurator.EplAddIn.Tool.csproj`
  - [x] Restore and verify NuGet packages

- [x] Namespace & Class Renaming
  - [x] Rename namespaces to `AutoSchematicConfigurator.EplAddIn.Tool` in all C# source files
  - [x] Update Ribbon Command Action registration from `TestAction` to `AutoSchematicConfiguratorAction`
  - [x] Update x:Class in WPF `ConfiguratorWindow.xaml` and code-behind
  - [x] Fix hardcoded json database file paths in the code-behind to point to `c:\Users\62789\source\repos\ASC.EplAddIn.Tool`

- [x] Rule Engine Upgrade (NCalc)
  - [x] Write evaluation service using NCalc
  - [x] Implement custom functions: `Concat(...)` and `Lookup_Breaker(...)`
  - [x] Replace simple mock condition checks in `ConfiguratorService.cs` with the new NCalc evaluator

- [x] EPLAN API Wrapper & Robustness
  - [x] Refine page creation and location structural routing
  - [x] Update Placeholder variable mapping and Value Set activation logic (`PlaceHolder.Apply()`)
  - [x] Wrap generation logic inside an EPLAN transaction with safety lock and progress indicators

- [x] Verification & Build
  - [x] Run `dotnet build` or `msbuild` to ensure clean compilation
  - [x] Create walkthrough documentation

- [x] Production Usability Tasks
  - [x] Implement EPLAN Document Type (&) support in GetOrCreatePage and page macro renaming
  - [x] Implement Window Macro Placeholder Isolation using oInsert.WindowMacro returned StorableObjects
  - [x] Register new helper functions (IsAvailable, If, IsNullEmpty, etc.) in the NCalc evaluator
  - [x] Implement ConvertToVariableType culture-aware converter helper
  - [x] Support default fallbacks for unmapped group variables
  - [x] Implement Window Macro RepresentationType and MoveKind overloads
  - [x] Add direct non-prefixed placeholder variable mapping compatibility
  - [x] Add generation-time validation for visible mandatory variables
  - [x] Log warning messages on NCalc evaluation failures
  - [x] Implement name-prefix based folder creation dialog
  - [x] Fix toolbar Copy button selected item resolution DataContext bug
  - [x] Fix toolbar Delete button selected item resolution DataContext bug
  - [x] Fix Page Macro and Window Macro active page fallback to Project Selection
  - [x] Inherit Page structural properties in temporary page creation to bypass structural checks
  - [x] Implement Window Macro RepresentationType.Default (0) overload selection
  - [x] Safely remove post-rollback page deletion inside database transaction abort handlers
  - [x] Fix GridSplitter jumping by adding MinWidth to main editor column and changing to Center alignment
  - [x] Bind TvMacros double-click selection and remove redundant BtnApplyMacroSelection button
  - [x] Fix page macro placeholder tree loading by supplying active EPLAN project context to pm.Open
  - [x] Unify all drag handles to 6-dot grips and generalise reordering code-behind to support ListBox controls
  - [x] Resolve variable drag offset misalignment by calculating coordinate translations relative to the global DragCanvas
  - [x] Fix page macro placeholder tree loading by implementing short-lived transaction insertion inside the macro explorer
  - [x] Fix DataGrid resize scrollbar flicker and layout jitter by disabling horizontal scrollbar
  - [x] Add null safety check in mouse release handler to prevent drag-drop reordering crashes


