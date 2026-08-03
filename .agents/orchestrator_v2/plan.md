# Project Plan — orchestrator_v2

## Task Overview
Coordinate the resolution of issues with performance, placeholders, page shaking, and feature removal in the EPLAN add-in:
- R1. Optimize Macro Selector First Load Speed
- R2. Load Placeholder Variables for Page Macros
- R3. Resolve UI Shaking when Resizing Navigator
- R4. Remove "Import from Disk" Feature

## Decomposed Milestones

### Milestone 1: Exploration and Analysis
- **Goal**: Analyze the codebase, locate target classes, files, and EPLAN APIs. Validate the build and test framework.
- **Worker/Role**: `teamwork_preview_explorer` (Explorer)
- **Scope**:
  - Scan the project for files related to macro selection and page querying.
  - Locate EPLAN finder calls (`finder.GetAll<Page>(true)`).
  - Check file-system operations inside macro directory scanning and how they can be made async.
  - Locate `LoadMacroFileDetails`, tree view nodes, and page macro insertion logic (`Insert.PageMacro`).
  - Locate `ConfiguratorWindow.xaml` and its layout grid definition.
  - Locate the "Import from Disk" button, context menu item "选择本地磁盘文件...", and event handlers (`BtnAddElementFromDisk_Click`, `BtnBrowseElementMacro_Click`) in `ConfiguratorWindow.xaml` and `ConfiguratorWindow.Designer.cs` / `.cs`.
  - Validate that we can run `dotnet build` and `dotnet test` successfully.

### Milestone 2: Implementation (R1 & R2)
- **Goal**: Implement R1 (Optimize Macro Selector First Load Speed) and R2 (Load Placeholder Variables for Page Macros).
- **Worker/Role**: `teamwork_preview_worker` (Worker) and `teamwork_preview_reviewer` (Reviewers)
- **Scope**:
  - Update EPLAN page query: change `finder.GetAll<Page>(true)` to `finder.GetAll<Page>(false)`.
  - Run directory scanning asynchronously on background thread (`Task.Run`). Keep necessary EPLAN API calls on the main thread (UI thread). Ensure a non-blocking UI loading experience.
  - Correct `LoadMacroFileDetails` to properly fetch page macro placeholders.
  - Pass valid active/target page instead of `null` to `Insert.PageMacro`.
  - Map parsed page placeholders by index (order of insertion) instead of exact page name string matching.
  - Ensure the tree selector displays the placeholder sub-nodes for page macros when expanded.
  - Verify that the changes compile and run properly.

### Milestone 3: Implementation (R3 & R4)
- **Goal**: Implement R3 (Resolve UI Shaking when Resizing Navigator) and R4 (Remove "Import from Disk" Feature).
- **Worker/Role**: `teamwork_preview_worker` (Worker) and `teamwork_preview_reviewer` (Reviewers)
- **Scope**:
  - Remove `MinWidth="300"` from center workspace column in `ConfiguratorWindow.xaml`.
  - Set `MinWidth="300"` on the child `Grid` container itself in `ConfiguratorWindow.xaml`.
  - Remove "从磁盘导入..." button from `ConfiguratorWindow.xaml`.
  - Remove context menu item "选择本地磁盘文件..." and its handlers from `ConfiguratorWindow.Designer.cs`.
  - Delete `BtnAddElementFromDisk_Click` and `BtnBrowseElementMacro_Click` methods.
  - Verify that the changes compile and run properly.

### Milestone 4: Verification and Final Audit
- **Goal**: Run complete test suite and forensic audit to ensure all changes are correct, clean, and pass existing 63 tests.
- **Worker/Role**: `teamwork_preview_challenger` (Challenger) and `teamwork_preview_auditor` (Auditor)
- **Scope**:
  - Ensure all 63 tests pass via `dotnet test`.
  - Perform static verification, check for code style compliance (PascalCase, private field `_camelCase` naming, 4-space indentation, no warnings/errors).
  - Verify no cheats, facades, or hardcoded mock expectations.
