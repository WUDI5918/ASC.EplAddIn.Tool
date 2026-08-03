# Critical Bug Fixes (Batch 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the seven highest-severity defects found in the code review (data files never shipped, formula string corruption, group-cycle stack overflow, stale sequential layout state, page-macro page collisions, version restore being a no-op, and destructive project overwrite) plus the four UI changes-not-saved defects.

**Architecture:** Each fix is small and localized: two csproj metadata entries; a parser-aware rewrite of `PreprocessNCalcSyntax`; a pure cycle-detection helper called at group-generation entry; one-line state reset; an unconditional page replacement in the `.emp` insertion path; wiring version restore through `VersionHistoryDialog` -> `LibrarySettingsDialog` -> `ConfiguratorWindow`; a guarded overwrite block in the silent Project Builder action; and `MarkDirty()`/value fixes in four window handlers. Unit tests are added where the logic is EPLAN-free; EPLAN-dependent fixes are verified by build plus manual repro steps.

**Tech Stack:** C# / WPF / .NET Framework 4.8.1, xUnit test project under `tests/`, Newtonsoft.Json, Jint, EPLAN API 2026.0.3.

## Global Constraints

- Target framework: `net481`, x64, WPF. Build with `dotnet build` from the repo root (solution default config is `Debug|Any CPU`; do NOT pass `-p:Platform=AnyCPU` — the slnx does not define that platform).
- Tests: xUnit, naming pattern `MethodName_Should_ExpectedBehavior`; run with `dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj`.
- Style: 4-space indent, PascalCase public members, `_camelCase` private fields, no tabs (`.editorconfig`).
- Formula semantics: expressions starting with `=` are evaluated by Jint; anything else is a static value. NCalc words `and/or/not` must translate to `&&/||/!` only outside string literals.
- Commit messages: conventional prefix (`fix:`, `test:`, `chore:`), first line under 72 chars.
- Do not change public EPLAN-facing behavior beyond the listed fixes; do not reformat unrelated code.

---

### Task 1: Ship `configurator_data.json` and `breaker_catalog.csv` with the build

**Background/Repro:** `GetDatabasePath()` and `GetCatalogPath()` look for these files next to the assembly, but the csproj never copies them to output. Verified: `bin\Debug` contains neither file, so at runtime the library loads empty and `Lookup_Breaker` always returns `Default_Set`.

**Files:**
- Modify: `Schematic AutoConfig.EplAddIn.Tool.csproj` (append an ItemGroup after the existing `<ItemGroup>` that contains the PackageReferences)

**Interfaces:**
- Consumes: nothing
- Produces: `bin\Debug\configurator_data.json` and `bin\Debug\breaker_catalog.csv` present after every build

- [ ] **Step 1: Add the copy rules to the project file**

```xml
  <ItemGroup>
    <None Update="configurator_data.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="breaker_catalog.csv" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Build and verify the files land in the output**

Run:
```powershell
dotnet build
Test-Path "bin\Debug\configurator_data.json"
Test-Path "bin\Debug\breaker_catalog.csv"
```
Expected: both `Test-Path` results are `True`.

- [ ] **Step 3: Commit**

```bash
git add "Schematic AutoConfig.EplAddIn.Tool.csproj"
git commit -m "fix: copy configurator data files to build output"
```

---

### Task 2: Stop `and/or/not` preprocessing from corrupting string literals

**Background/Repro:** `PreprocessNCalcSyntax` uses three global `Regex.Replace` calls with word boundaries, which also rewrite words inside quoted strings. Verified with the same regex: `Concat('Start and Stop')` becomes `Concat('Start && Stop')`, silently corrupting any text value containing `and`, `or`, or `not`.

**Files:**
- Modify: `ConfiguratorService.Evaluation.cs` (replace `PreprocessNCalcSyntax`, lines 25-34; add `using System.Text;`)
- Test: `tests/ValidationAndFormulasTests.cs` (append to the existing test class)

**Interfaces:**
- Consumes: existing `ConfiguratorService.EvaluateExpression(string, Dictionary<string, object>)`
- Produces: `internal static string PreprocessNCalcSyntax(string expression)` — now string-literal-aware; same public contract as before

- [ ] **Step 1: Write the failing tests**

Add to `tests/ValidationAndFormulasTests.cs`:

```csharp
[Fact]
public void EvaluateExpression_WithAndOrNotInsideStringLiterals_ShouldNotRewriteThem()
{
    var parameters = new Dictionary<string, object>();

    Assert.Equal("Start and Stop", ConfiguratorService.EvaluateExpression("Concat('Start and Stop')", parameters)?.ToString());
    Assert.Equal("black or white", ConfiguratorService.EvaluateExpression("Concat('black or white')", parameters)?.ToString());
    Assert.Equal("do not", ConfiguratorService.EvaluateExpression("Concat('do not')", parameters)?.ToString());
    Assert.Equal("A and B", ConfiguratorService.EvaluateExpression("Concat(\"A and B\")", parameters)?.ToString());
}

[Fact]
public void EvaluateExpression_WithNCalcWordsOutsideStrings_ShouldStillConvert()
{
    var parameters = new Dictionary<string, object>
    {
        { "A", true },
        { "B", false }
    };

    Assert.False((bool)ConfiguratorService.EvaluateExpression("A and B", parameters));
    Assert.True((bool)ConfiguratorService.EvaluateExpression("A or B", parameters));
    Assert.True((bool)ConfiguratorService.EvaluateExpression("not A == false", parameters));
    Assert.Equal("android", ConfiguratorService.EvaluateExpression("Concat('android')", parameters)?.ToString());
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```powershell
dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj --filter "EvaluateExpression_WithAndOrNotInsideStringLiterals_ShouldNotRewriteThem"
```
Expected: FAIL (actual value is `Start && Stop`).

- [ ] **Step 3: Replace `PreprocessNCalcSyntax` with a parser-aware implementation**

Replace the whole method in `ConfiguratorService.Evaluation.cs` (and add `using System.Text;` at the top of the file):

```csharp
internal static string PreprocessNCalcSyntax(string expression)
{
    if (string.IsNullOrEmpty(expression)) return expression;

    var sb = new StringBuilder(expression.Length);
    char quote = '\0';
    int i = 0;

    while (i < expression.Length)
    {
        char c = expression[i];

        // Inside a string literal: copy verbatim, honoring backslash escapes.
        if (quote != '\0')
        {
            sb.Append(c);
            if (c == '\\' && i + 1 < expression.Length)
            {
                sb.Append(expression[i + 1]);
                i += 2;
                continue;
            }
            if (c == quote) quote = '\0';
            i++;
            continue;
        }

        if (c == '\'' || c == '"')
        {
            quote = c;
            sb.Append(c);
            i++;
            continue;
        }

        if (char.IsLetter(c))
        {
            int start = i;
            while (i < expression.Length && IsWordChar(expression[i])) i++;

            string word = expression.Substring(start, i - start);
            bool boundaryBefore = start == 0 || !IsWordChar(expression[start - 1]);
            bool boundaryAfter = i >= expression.Length || !IsWordChar(expression[i]);

            if (boundaryBefore && boundaryAfter)
            {
                if (word.Equals("and", StringComparison.OrdinalIgnoreCase)) word = "&&";
                else if (word.Equals("or", StringComparison.OrdinalIgnoreCase)) word = "||";
                else if (word.Equals("not", StringComparison.OrdinalIgnoreCase)) word = "!";
            }

            sb.Append(word);
            continue;
        }

        sb.Append(c);
        i++;
    }

    return sb.ToString();
}

private static bool IsWordChar(char c)
{
    return char.IsLetterOrDigit(c) || c == '_';
}
```

- [ ] **Step 4: Run the full test suite**

Run:
```powershell
dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj
```
Expected: all tests pass (the two new tests plus the existing 69; the pre-existing `EvaluateExpression_WithNCalcAndOr_ShouldEvaluate` tests must still pass).

- [ ] **Step 5: Commit**

```bash
git add ConfiguratorService.Evaluation.cs tests/ValidationAndFormulasTests.cs
git commit -m "fix: keep and/or/not inside string literals intact"
```

---

### Task 3: Detect Typical-Group cycles before generation

**Background/Repro:** `AllTypicalTargets` only excludes the current group itself, so the UI lets you build A -> B -> A. `GenerateTypicalGroup` recurses with no visited set; generation then recurses until stack overflow inside EPLAN. StackOverflowException cannot be caught and takes the process down.

**Files:**
- Modify: `ConfiguratorService.Generator.cs` (add public helper next to `GenerateTypicalGroup`; add a guard at the top of `GenerateTypicalGroup`)
- Test: create `tests/CycleDetectionTests.cs`

**Interfaces:**
- Consumes: `ConfiguratorData`, `TypicalGroup`, `TypicalInstance` from `ConfiguratorModel.cs`
- Produces: `public static bool HasTypicalGroupCycle(TypicalGroup startGroup, ConfiguratorData db)` — returns `true` when `startGroup` transitively contains itself via `TypicalGroup` instances

- [ ] **Step 1: Write the failing tests**

Create `tests/CycleDetectionTests.cs`:

```csharp
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class CycleDetectionTests
    {
        [Fact]
        public void HasTypicalGroupCycle_WithSelfReference_ShouldReturnTrue()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "A", Type = "TypicalGroup" });
            db.TypicalGroups.Add(groupA);

            Assert.True(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }

        [Fact]
        public void HasTypicalGroupCycle_WithIndirectCycle_ShouldReturnTrue()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            var groupB = new TypicalGroup { Name = "B" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "B", Type = "TypicalGroup" });
            groupB.Instances.Add(new TypicalInstance { TargetName = "A", Type = "TypicalGroup" });
            db.TypicalGroups.Add(groupA);
            db.TypicalGroups.Add(groupB);

            Assert.True(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }

        [Fact]
        public void HasTypicalGroupCycle_WithAcyclicGroups_ShouldReturnFalse()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            var groupB = new TypicalGroup { Name = "B" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "B", Type = "TypicalGroup" });
            db.TypicalGroups.Add(groupA);
            db.TypicalGroups.Add(groupB);

            Assert.False(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }

        [Fact]
        public void HasTypicalGroupCycle_WithMacroTypicalOnly_ShouldReturnFalse()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "M1", Type = "MacroTypical" });
            db.TypicalGroups.Add(groupA);

            Assert.False(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```powershell
dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj --filter "HasTypicalGroupCycle"
```
Expected: FAIL (member not found / compile error).

- [ ] **Step 3: Add the cycle-detection helper and guard**

Insert the helper immediately before the `GenerateTypicalGroup` method in `ConfiguratorService.Generator.cs`:

```csharp
public static bool HasTypicalGroupCycle(TypicalGroup startGroup, ConfiguratorData db)
{
    if (startGroup == null || db?.TypicalGroups == null) return false;

    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    return HasTypicalGroupCycleCore(startGroup, db, visited, stack);
}

private static bool HasTypicalGroupCycleCore(TypicalGroup group, ConfiguratorData db, HashSet<string> visited, HashSet<string> stack)
{
    if (group == null) return false;
    if (!visited.Add(group.Name)) return false;
    stack.Add(group.Name);

    if (group.Instances != null)
    {
        foreach (var instance in group.Instances)
        {
            if (!instance.Type.Equals("TypicalGroup", StringComparison.OrdinalIgnoreCase)) continue;

            var child = db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
            if (child == null) continue;
            if (stack.Contains(child.Name)) return true;
            if (HasTypicalGroupCycleCore(child, db, visited, stack)) return true;
        }
    }

    stack.Remove(group.Name);
    return false;
}
```

Then add the guard at the very top of `GenerateTypicalGroup` (before `if (group.Instances == null) return;`):

```csharp
if (HasTypicalGroupCycle(group, db))
{
    Log("Error", $"检测到典型组循环引用: '{group.Name}' 直接或间接引用了自身，已终止生成。");
    throw new InvalidOperationException($"检测到典型组循环引用: '{group.Name}' 直接或间接引用了自身，已终止生成。");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```powershell
dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add ConfiguratorService.Generator.cs tests/CycleDetectionTests.cs
git commit -m "fix: abort generation on typical-group cycles"
```

---

### Task 4: Clear sequential-layout bounding boxes in the single-typical path

**Background/Repro:** `GenerateConfigurator` clears `_pageBoundingBoxes` before generation, but `GenerateTypical` (single macro-typical via the "生成" button) does not. Running the same typical twice makes the second run place macros from the previous run's bounding box instead of `element.X/Y`.

**Files:**
- Modify: `ConfiguratorService.cs` (line 173 area)

**Interfaces:**
- Consumes: nothing
- Produces: `_pageBoundingBoxes` empty at the start of every `GenerateTypical` invocation

- [ ] **Step 1: Add the missing reset**

In `GenerateTypical`, replace:

```csharp
            ClearedPages.Clear();
```

with:

```csharp
            ClearedPages.Clear();
            _pageBoundingBoxes.Clear();
```

- [ ] **Step 2: Build and manually verify**

Run:
```powershell
dotnet build
```
Expected: build succeeds.

Manual repro (EPLAN): open a project, create a typical with two window macros both set to `Sequential` positioning, generate twice. Before the fix the second run's second macro overlaps the first run's positions; after the fix both runs start from `element.X/Y` on the first macro.

- [ ] **Step 3: Commit**

```bash
git add ConfiguratorService.cs
git commit -m "fix: reset sequential layout bounds in single-typical generation"
```

---

### Task 5: Make `.emp` page macros always own their target pages

**Background/Repro:** In `GenerateTypicalInternal`, the `.emp` path skips deleting an existing target page when `ClearedPages` already contains the page name. If two page macros target the same page name (e.g., both use `PageName=101`), the second macro's placeholders are applied to the first macro's pages, `placedCount` over-counts, and `lastEmpPage` points at the wrong page.

**Files:**
- Modify: `ConfiguratorService.Generator.cs` (lines 449-465, the delete-existing-page block in the `.emp` branch)

**Interfaces:**
- Consumes: `ClearedPages`, `FormatPageName`, existing `.emp` numbering logic
- Produces: for each macro page, any pre-existing project page with the same full name is removed before insertion; `ClearedPages` still records the name so later window macros reuse the fresh page

- [ ] **Step 1: Make target-page deletion unconditional in the `.emp` branch**

Replace the block:

```csharp
                                if (!ClearedPages.Contains(fullTargetPageName))
                                {
                                    Page existingTarget = activeProject.Pages.FirstOrDefault(pageObj => pageObj.Name.Equals(fullTargetPageName, StringComparison.OrdinalIgnoreCase));
                                    if (existingTarget != null)
                                    {
                                        Log("Info", $"  检测到目标页面已存在 '{fullTargetPageName}'，正在清理以重新生成...");
                                        try
                                        {
                                            existingTarget.Remove();
                                        }
                                        catch (Exception remEx)
                                        {
                                            Log("Warning", $"  清理页面 '{fullTargetPageName}' 失败: {remEx.Message}");
                                        }
                                    }
                                    ClearedPages.Add(fullTargetPageName);
                                }
```

with:

```csharp
                                // Always remove a pre-existing page with this name so the
                                // page macro owns its pages; placeholders below must target
                                // the freshly inserted pages, not an earlier macro's pages.
                                Page existingTarget = activeProject.Pages.FirstOrDefault(pageObj => pageObj.Name.Equals(fullTargetPageName, StringComparison.OrdinalIgnoreCase));
                                if (existingTarget != null)
                                {
                                    Log("Info", $"  检测到目标页面已存在 '{fullTargetPageName}'，正在清理以重新生成...");
                                    try
                                    {
                                        existingTarget.Remove();
                                    }
                                    catch (Exception remEx)
                                    {
                                        Log("Warning", $"  清理页面 '{fullTargetPageName}' 失败: {remEx.Message}");
                                    }
                                }
                                ClearedPages.Add(fullTargetPageName);
```

Note: keep the existing post-insertion re-find code (lines 493-518) unchanged — it resolves the fresh pages by name after the delete, which is now guaranteed unambiguous.

- [ ] **Step 2: Build and manually verify**

Run:
```powershell
dotnet build
```
Expected: build succeeds.

Manual repro (EPLAN): create two macro-typical elements, both `.emp` page macros with `PageName=101` and different placeholder values. Generate. After the fix, the project contains one page `.../101` containing the second macro's content/placeholders, and the generation log shows only the second macro's placeholder updates for that page.

- [ ] **Step 3: Commit**

```bash
git add ConfiguratorService.Generator.cs
git commit -m "fix: page macros replace colliding target pages"
```

---

### Task 6: Make version restore actually restore data

**Background/Repro:** `VersionHistoryDialog` returns `RestoredData`/`WasRestored`, but `LibrarySettingsDialog.BtnVersionHistory_Click` ignores them — clicking "还原" shows a success message and changes nothing. Additionally, version snapshots are created from the on-disk file, so unsaved edits are never snapshotted.

**Files:**
- Modify: `VersionHistoryDialog.xaml.cs` (constructor, `BtnCreateVersion_Click`, `BtnRestore_Click`)
- Modify: `LibrarySettingsDialog.xaml.cs` (constructor, `BtnVersionHistory_Click`)
- Modify: `ConfiguratorWindow.ProjectBuilder.cs` (`BtnLibrarySettings_Click`)

**Interfaces:**
- Consumes: `ConfiguratorData`, `LibrarySettings`
- Produces:
  - `VersionHistoryDialog(ConfiguratorData currentDb = null, Action<ConfiguratorData> onRestored = null)` — snapshots from `currentDb` when provided; invokes `onRestored` after a successful restore
  - `LibrarySettingsDialog(LibrarySettings settings, ConfiguratorData currentDb = null, Action<ConfiguratorData> onVersionRestored = null)` — passes `currentDb` through to the history dialog and applies restored settings to itself

- [ ] **Step 1: Update `VersionHistoryDialog`**

Add two fields and change the constructor:

```csharp
        private readonly ConfiguratorData _currentDb;
        private readonly Action<ConfiguratorData> _onRestored;

        public VersionHistoryDialog(ConfiguratorData currentDb = null, Action<ConfiguratorData> onRestored = null)
        {
            InitializeComponent();
            _currentDb = currentDb;
            _onRestored = onRestored;
            LoadVersions();
        }
```

In `BtnCreateVersion_Click`, replace the file-reading block:

```csharp
                // 加载当前数据库
                string dbPath = ConfiguratorService.GetDatabasePath();
                ConfiguratorData db;
                if (File.Exists(dbPath))
                {
                    string json = File.ReadAllText(dbPath);
                    db = JsonConvert.DeserializeObject<ConfiguratorData>(json) ?? new ConfiguratorData();
                }
                else
                {
                    MessageBox.Show("当前库文件不存在，无法创建版本。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
```

with:

```csharp
                // Use the in-memory database when provided, so unsaved edits are snapshotted.
                ConfiguratorData db = _currentDb;
                if (db == null)
                {
                    string dbPath = ConfiguratorService.GetDatabasePath();
                    if (!File.Exists(dbPath))
                    {
                        MessageBox.Show("当前库文件不存在，无法创建版本。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    db = JsonConvert.DeserializeObject<ConfiguratorData>(File.ReadAllText(dbPath)) ?? new ConfiguratorData();
                }
```

In `BtnRestore_Click`, after the existing lines:

```csharp
                RestoredData = data;
                WasRestored = true;
```

add:

```csharp
                _onRestored?.Invoke(data);
```

- [ ] **Step 2: Update `LibrarySettingsDialog`**

Add two fields, change the constructor, and update `BtnVersionHistory_Click`:

```csharp
        private readonly ConfiguratorData _currentDb;
        private readonly Action<ConfiguratorData> _onVersionRestored;

        public LibrarySettingsDialog(LibrarySettings settings, ConfiguratorData currentDb = null, Action<ConfiguratorData> onVersionRestored = null)
        {
            InitializeComponent();
            Settings = settings ?? new LibrarySettings();
            _currentDb = currentDb;
            _onVersionRestored = onVersionRestored;
            LoadSettings();
        }
```

```csharp
        private void BtnVersionHistory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VersionHistoryDialog(_currentDb, _onVersionRestored);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.WasRestored && dlg.RestoredData != null)
            {
                Settings = dlg.RestoredData.Settings ?? new LibrarySettings();
                LoadSettings();
            }
        }
```

- [ ] **Step 3: Update `ConfiguratorWindow.BtnLibrarySettings_Click`**

Replace:

```csharp
            var dialog = new LibrarySettingsDialog(_db.Settings);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _db.Settings = dialog.Settings;
                MarkDirty();
                MessageBox.Show("库属性设置已保存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
```

with:

```csharp
            var dialog = new LibrarySettingsDialog(_db.Settings, _db, restored =>
            {
                _db = restored ?? new ConfiguratorData();
                if (_db.Settings == null) _db.Settings = new LibrarySettings();
                InvalidateTypicalTargetsCache();
                RefreshSidebar();
                UpdateWorkspaceSelection();
                MarkDirty();
            });
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _db.Settings = dialog.Settings;
                MarkDirty();
                MessageBox.Show("库属性设置已保存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
```

- [ ] **Step 4: Build and manually verify**

Run:
```powershell
dotnet build
dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj
```
Expected: build succeeds; all tests pass.

Manual repro: open library settings -> version history -> create a version (with an unsaved edit in the designer), then make a destructive change, then restore the version. Expected: the designer sidebar and workspace revert to the snapshot and the status bar shows unsaved changes; closing and reopening keeps the restored data.

- [ ] **Step 5: Commit**

```bash
git add VersionHistoryDialog.xaml.cs LibrarySettingsDialog.xaml.cs ConfiguratorWindow.ProjectBuilder.cs
git commit -m "fix: wire version restore to the live library"
```

---

### Task 7: Make Project Builder overwrite non-destructive

**Background/Repro:** With `overwrite=1`, `ExecuteCogineerProjectBuilderAction` deletes the `.elk`/`.edb` project even when no template is supplied, then returns `false` — the old project is gone and nothing replaced it. If the project is already open, the delete is skipped entirely (overwrite silently ignored) because the open-project loop assigns it before the overwrite branch runs.

**Files:**
- Modify: `AddInMenuModel.cs` (lines 409-461)

**Interfaces:**
- Consumes: `ActionCallingContext` parameters `overwrite`, `template`, `storagelocation`, `targetProjectName`
- Produces: overwrite only proceeds when a valid template exists; an open project is closed before deletion

- [ ] **Step 1: Skip the open-project shortcut when overwriting**

Replace:

```csharp
                        if (string.Equals(openProj.ProjectName, targetProjectName, StringComparison.OrdinalIgnoreCase))
                        {
                            project = openProj;
                            break;
                        }
```

with:

```csharp
                        if (string.Equals(openProj.ProjectName, targetProjectName, StringComparison.OrdinalIgnoreCase) && !overwrite)
                        {
                            project = openProj;
                            break;
                        }
```

- [ ] **Step 2: Require a template and close the open project before deleting**

Replace:

```csharp
                            if (overwrite)
                            {
                                try
                                {
                                    string dirPath = projectPath.Substring(0, projectPath.Length - 4) + ".edb";
                                    if (Directory.Exists(dirPath)) Directory.Delete(dirPath, true);
                                    if (File.Exists(projectPath)) File.Delete(projectPath);
                                }
                                catch (Exception dex)
                                {
                                    ConfiguratorService.LogDebug($"Warning: Failed to delete existing project for overwrite: {dex.Message}");
                                }
                            }
```

with:

```csharp
                            if (overwrite)
                            {
                                if (string.IsNullOrEmpty(template) || !File.Exists(template))
                                {
                                    ConfiguratorService.LogDebug("Error: overwrite requires a valid template to recreate the project.");
                                    return false;
                                }

                                try
                                {
                                    foreach (Project openProj in pm.OpenProjects)
                                    {
                                        if (string.Equals(openProj.ProjectName, targetProjectName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            openProj.Close();
                                            break;
                                        }
                                    }

                                    string dirPath = projectPath.Substring(0, projectPath.Length - 4) + ".edb";
                                    if (Directory.Exists(dirPath)) Directory.Delete(dirPath, true);
                                    if (File.Exists(projectPath)) File.Delete(projectPath);
                                }
                                catch (Exception dex)
                                {
                                    ConfiguratorService.LogDebug($"Error: Failed to delete existing project for overwrite: {dex.Message}");
                                    return false;
                                }
                            }
```

- [ ] **Step 3: Build and manually verify**

Run:
```powershell
dotnet build
```
Expected: build succeeds.

Manual repro (silent action): (a) `overwrite=1` with a valid template and an existing target -> project is closed, deleted, recreated from template, generation runs; (b) `overwrite=1` with no template -> action returns `false` and the existing project still exists; (c) target open in EPLAN with `overwrite=1` and template -> open instance is closed first, then recreated.

- [ ] **Step 4: Commit**

```bash
git add AddInMenuModel.cs
git commit -m "fix: require template before overwriting projects"
```

---

### Task 8: Persist rename and mapping edits; fix share-variable value

**Background/Repro:** Renaming an item in `TxtSelectedName_LostFocus` never calls `MarkDirty()`; adding/removing placeholder mapping rows (`BtnAddVarMapping_Click`, `BtnDeleteVarMapping_Click`) never calls `MarkDirty()`; `BtnShareVariable_Click` sets `mapping.Value = mapping.Key` (a static literal equal to the variable name, not the variable's value) and never calls `MarkDirty()`.

**Files:**
- Modify: `ConfiguratorWindow.xaml.cs` (`TxtSelectedName_LostFocus`)
- Modify: `ConfiguratorWindow.MacroNavigator.cs` (`BtnAddVarMapping_Click`, `BtnShareVariable_Click`, `BtnDeleteVarMapping_Click`)

**Interfaces:**
- Consumes: `MarkDirty()`, `MappingEntry`, `ConfigurationVariable`, `BindVariablesList`
- Produces: all four handlers leave the window dirty; `BtnShareVariable_Click` creates a variable named after the placeholder's variable part and maps `=VarName`

- [ ] **Step 1: Mark renames as dirty**

In `TxtSelectedName_LostFocus`, after the existing `RefreshSidebar();` add:

```csharp
            MarkDirty();
```

- [ ] **Step 2: Mark mapping add/delete as dirty**

In `BtnAddVarMapping_Click`, after the existing `element.VariableMappings.Add(new MappingEntry("新占位符", ""));` add:

```csharp
                    MarkDirty();
```

In `BtnDeleteVarMapping_Click`, after the existing `element.VariableMappings.Remove(entry);` add:

```csharp
                    MarkDirty();
```

- [ ] **Step 3: Fix share-variable semantics**

Replace the body of `BtnShareVariable_Click`:

```csharp
        private void BtnShareVariable_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var mapping = btn.DataContext as MappingEntry;
            if (mapping == null) return;

            if (!(_selectedItem is MacroTypical typical)) return;

            // The mapping key may be "PhName=VarName", "PhName : VarName", or just "VarName".
            string varName = mapping.Key ?? "";
            int sep = varName.LastIndexOfAny(new[] { '=', ':' });
            if (sep >= 0 && sep < varName.Length - 1)
            {
                varName = varName.Substring(sep + 1).Trim();
            }
            if (string.IsNullOrWhiteSpace(varName)) return;

            var existingVar = typical.Variables.FirstOrDefault(v => string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase));
            if (existingVar == null)
            {
                typical.Variables.Add(new ConfigurationVariable
                {
                    Name = varName,
                    Type = "String",
                    DisplayName = varName
                });
            }

            mapping.Value = "=" + varName;
            BindVariablesList(typical.Variables);
            MarkDirty();
        }
```

- [ ] **Step 4: Build and manually verify**

Run:
```powershell
dotnet build
dotnet test tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj
```
Expected: build succeeds; all tests pass.

Manual repro: (a) rename a macro typical, close and reopen the window -> name persists; (b) add a mapping row, close and reopen -> row persists; (c) click "share variable" on a mapping `Motor=Power` -> a `Power` variable appears and the mapping value shows `=Power`; after generation the placeholder receives the runtime value of `Power`, not the literal text `Power`.

- [ ] **Step 5: Commit**

```bash
git add ConfiguratorWindow.xaml.cs ConfiguratorWindow.MacroNavigator.cs
git commit -m "fix: persist renames and mapping edits"
```

---

## Self-Review

**Spec coverage:** The seven high-severity findings map 1:1 to Tasks 1-7; the four UI save/value defects map to Task 8. Medium/low-severity findings (XLSX round-trip loss, boolean ComboBox typing, concurrent static caches, duplicate JSON settings, dangling-reference deletes, formula-editor static values) are intentionally deferred to a Batch 2 plan.

**Placeholder scan:** No TBD/TODO steps; every code step contains complete code and every verification step contains the exact command and expected result.

**Type consistency:** `HasTypicalGroupCycle(TypicalGroup, ConfiguratorData)` is defined in Task 3 and only used there; `VersionHistoryDialog(ConfiguratorData, Action<ConfiguratorData>)` and `LibrarySettingsDialog(LibrarySettings, ConfiguratorData, Action<ConfiguratorData>)` match across Task 6 steps; `MarkDirty()` and `BindVariablesList` already exist and are used unchanged.
