# Original User Request

## Initial Request — 2026-07-02T08:34:29+08:00

Re-examine and analyze all features of the local EPLAN schematic generator add-in, achieving full parity with EPLAN ebuilder automatic schematic generation capabilities, ensuring it is production-ready and fully functional rather than simulated, without changing the existing UI and framework.

Working directory: c:\Users\62789\source\repos\ASC.EplAddIn.Tool
Integrity mode: development

## Requirements

### R1. Complete Page Structure & Designation Support
Support both EPLAN Document Type (`&`) and User-Defined (`#`) structure identifiers inside the page creation logic.
- Extract `&` from mappings and apply to the page's native `DESIGNATION_DOCTYPE` property.
- Extract `#` (or the key `UserDefined`) from mappings and apply to the page's native `DESIGNATION_USERDEFINED` property.
- Format full page names correctly with all active structure prefixes (e.g., `==ESS=L1+P1&EBH#USER/101`).

### R2. Fix JSON Settings Deserialization List Duplication
Fix the bug where the `SelectedLanguages` and `AvailableStructureIdentifiers` list properties in the `LibrarySettings` class duplicate/multiply each time the configurator database is loaded and saved. Use `ObjectCreationHandling.Replace` during NewtonSoft.Json deserialization.

### R3. Flexible Placeholder Variable Name Matching
Expand placeholder variable name mapping to support additional separator patterns. Support mapping matching keys like `PlaceholderName.VariableName` and `PlaceholderName:VariableName` (without surrounding spaces) to cover typical EPLAN engineering placeholder design styles.

### R4. Generation-time Validation and Formula Stability
Ensure that all visible mandatory variables are strictly checked at generation time, throwing clear and descriptive errors if they are null, empty, or fail evaluation. Enhance exception handling to catch evaluation errors gracefully, writing them to both the WPF Status display and the EPLAN add-in debug logs.

## Acceptance Criteria

### Compilation & Code Quality
- [ ] The solution must build successfully via `dotnet build` without warnings or errors.
- [ ] Coding style must follow the repo standard: PascalCase for public members, `_camelCase` for private fields, and 4-space indentation.

### Functional Correctness
- [ ] EPLAN Page structure mapping accurately assigns `DESIGNATION_DOCTYPE` for `&` and `DESIGNATION_USERDEFINED` for `#` on generated Page objects.
- [ ] Target page names are formatted correctly inside EPLAN projects (e.g., `==ASSIGN=PLANT+LOC&DOC#USER/PAGE`).
- [ ] Duplicated settings identifiers in `configurator_data.json` are resolved and no longer duplicate upon loading/saving the database.
- [ ] Placeholders mapping resolves formats like `Placeholder.Var` and `Placeholder:Var` correctly.
- [ ] Missing or invalid mandatory variables fail generation cleanly and display detailed validation messages.

## Follow-up — 2026-07-02T09:45:49+08:00

The goal of this task is to resolve issues with performance, placeholders, page shaking, and feature removal in the EPLAN add-in.

Working directory: c:\Users\62789\source\repos\ASC.EplAddIn.Tool
Integrity mode: development

## Requirements

### R1. Optimize Macro Selector First Load Speed
- Change EPLAN page query (`finder.GetAll<Page>(true)`) to use `false` (do not load storable child objects) to avoid heavy API load.
- Extract file-system operations (like directory scanning of project-specific macro subdirectory inside `$(MD_MACROS)`) to run asynchronously on a background thread (`Task.Run`), keeping only necessary EPLAN API calls on the main thread.
- Provide a non-blocking UI loading experience.

### R2. Load Placeholder Variables for Page Macros
- Correct the logic in `LoadMacroFileDetails` to properly fetch page macro placeholders.
- Pass a valid target/active page instead of `null` to EPLAN's `Insert.PageMacro` so page insertion does not fail.
- Map the parsed page placeholders by index (order of insertion) rather than relying on exact page name string matching, to avoid structural naming conflicts.
- Ensure the tree selector displays the placeholder sub-nodes for page macros when expanded.

### R3. Resolve UI Shaking when Resizing Navigator
- Remove the `MinWidth="300"` constraint from the center workspace column definition for the Main Editor Panel in `ConfiguratorWindow.xaml`.
- Set `MinWidth="300"` on the child `Grid` container itself to prevent WPF GridSplitter layout loops and shaking during drag/resize.

### R4. Remove "Import from Disk" Feature
- Remove the "从磁盘导入..." button from the bottom of the Macro lists in `ConfiguratorWindow.xaml`.
- Remove the context menu item "选择本地磁盘文件..." and its handlers from `ConfiguratorWindow.Designer.cs`.
- Delete the unused `BtnAddElementFromDisk_Click` and `BtnBrowseElementMacro_Click` methods to clean up the codebase.

## Acceptance Criteria

### Correctness & Performance
- [ ] First load of the macro selector is fast (< 1 second) and does not lock/freeze the WPF window UI thread.
- [ ] Tree view expands page macro (.emp) nodes and displays all child placeholder variables correctly.
- [ ] Resizing/dragging the left pane splitter inward (making the navigator wider or narrower) is smooth and does not shake/jitter.
- [ ] The "从磁盘导入..." button and context menu item "选择本地磁盘文件..." are completely removed.
- [ ] Build completes successfully, and all existing 63 tests pass.
