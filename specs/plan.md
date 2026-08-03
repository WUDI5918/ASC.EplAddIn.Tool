# AutoSchematicConfigurator - Technical Plan

This document outlines the changes needed to rename the plugin, integrate NCalc and Newtonsoft.Json, upgrade the rules engine to handle complex formulas, and refine the EPLAN API wrappers.

## 1. Project-Level Modifications

### 1.1 Solution Mapping File
- **Modify** `AutoSchematicConfigurator.EplAddIn.Tool.slnx`: Update project path from `ElectricalSchematicConfigurator.EplAddIn.Tool.csproj` to `AutoSchematicConfigurator.EplAddIn.Tool.csproj`.

### 1.2 Project File (`AutoSchematicConfigurator.EplAddIn.Tool.csproj`)
- Change `<RootNamespace>` to `AutoSchematicConfigurator.EplAddIn.Tool`.
- Change `<AssemblyName>` to `AutoSchematicConfigurator.EplAddIn.Tool`.
- Add NuGet package references for NCalc, Newtonsoft.Json, and EPPlus:
  ```xml
  <ItemGroup>
    <PackageReference Include="NCalc-Edge" Version="1.4.3" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="EPPlus" Version="4.5.3.3" />
  </ItemGroup>
  ```
  *(Note: We use NCalc-Edge or NCalc which compiles well on .NET 4.8. NCalc is a good fit. We will add NuGet references inside the csprog).*

---

## 2. Namespace & Class Refactoring
We will rename the namespace `MyFirst.EplAddIn.Test` to `AutoSchematicConfigurator.EplAddIn.Tool` in the following files:
1. **`AddInMenuModel.cs`**:
   - Rename namespace.
   - Update `ActionName` constant and register string to `"AutoSchematicConfiguratorAction"`.
   - Rename action class from `TestAction` to `AutoSchematicConfiguratorAction`.
2. **`ConfiguratorModel.cs`**:
   - Rename namespace.
3. **`ConfiguratorService.cs`**:
   - Rename namespace.
4. **`ConfiguratorWindow.xaml.cs`**:
   - Rename namespace.
   - Update database path from `c:\Users\62789\source\repos\MyFirst.EplAddIn.Test` to `c:\Users\62789\source\repos\ASC.EplAddIn.Tool`.
5. **`ConfiguratorWindow.xaml`**:
   - Update `x:Class` to `AutoSchematicConfigurator.EplAddIn.Tool.ConfiguratorWindow`.

---

## 3. Rules Engine Implementation
We will replace the mock formula parsing in `ConfiguratorService.cs` with NCalc.
- Create a `RuleEngine` class or helper methods in `ConfiguratorService.cs`.
- Register the following custom functions:
  1. `Lookup_Breaker(power, startType)`: Queries a local selection method (e.g. SQLite, JSON lookup, or hardcoded engineering table logic) to return the correct Value Set string.
  2. `Concat(arg1, arg2, ...)`: Performs string concatenation for dynamic location and placeholder mapping.
- Variables will be mapped to parameters in NCalc (`e.Parameters[varName] = value`).
- Parse conditions like `MotorType == 'Direct' && Power_kW <= 15`.

---

## 4. EPLAN API Interaction Wrappers
We will refine:
- Page macro vs Window macro differentiation.
- Location and page structure handling (e.g., creating higher-level designation `==`, plant designation `=`, location designation `+`, and page name).
- Placeholder updates: Using `PlaceHolder.SetValue(varName, value)` and `PlaceHolder.SelectValueSet(setName)`, then calling `PlaceHolder.Apply()` to ensure graphic updates.
- Security and transaction control using EPLAN's `TransactionManager` and `LockingStep` to roll back changes upon error.

### 4.1 EPLAN Document Type (&) Implementation
- Map UI mapping key `UserDefined` or `&` in the `StructureMappings` to EPLAN page property `DESIGNATION_DOCTYPE`.
- In `GetOrCreatePage` and `GenerateTypicalInternal`, concatenate Document Type to page path using `&` (e.g. `==ESS=L1+P1&EBH/101`).

### 4.2 Window Macro Placeholder Isolation Implementation
- For Window Macros, obtain the array of placed objects `StorableObject[] insertedObjects` returned by `Insert.WindowMacro`.
- Filter placeholders using `insertedObjects.OfType<PlaceHolder>()` to only process placeholders belonging to the newly placed macro, leaving other macro placeholders on the same page unaffected.

### 4.3 Custom Formula Helper Registration
- Register the following additional functions in `EvaluateExpression`:
  - `IsAvailable(var)`: Returns whether parameter exists and is not null.
  - `If(cond, t, f)`: Inline ternary.
  - `IsNullEmpty(str)`: String null/empty check.
  - `Contains(str, sub)`: String contains check.
  - `StartsWith(str, sub)`: String starts with check.
  - `EndsWith(str, sub)`: String ends with check.

### 4.4 Culture-Aware Type Conversion & Default Fallbacks
- Create a private `ConvertToVariableType(object value, string targetType)` helper in `ConfiguratorService.Generator.cs`. Use it for converting evaluated variable values safely.
- If a child typical variable has no mapping in the parent typical group instance, retrieve the default value using `variable.DefaultValue` as evaluated input.

### 4.5 Window Macro RepresentationType & MoveKind Selection
- In `GenerateTypicalInternal`, cast `element.RepresentationType` to `WindowMacro.Enums.RepresentationType` and supply it to the `Insert.WindowMacro` overload.
- Inspect `element.PositionMode`. If equal to `"Absolute"`, set the placement move kind parameter to `Insert.MoveKind.Absolute` instead of `Relative`.

### 4.6 direct Placeholder Variable Name Matching
- In the placeholder variable application logic, support matches against the bare variable name: `m.Key.Equals(varName, StringComparison.OrdinalIgnoreCase)`.

### 4.7 Mandatory Variable Validation
- Validate mandatory typical/group parameters at generation-time. Throw an descriptive `ArgumentException` if a visible mandatory variable resolves to null or whitespace.
### 4.8 DataGrid Layout Stabilization
- Add `HorizontalScrollBarVisibility="Disabled"` to the `ItrElements` DataGrid in `ConfiguratorWindow.xaml` to resolve the layout measurement jitter caused by dynamic scrollbar toggling during splitter drag-resize.

### 4.9 Drag-Drop Null-Safety
- Add `if (_dragFloatingCard == null)` checks early in `General_PreviewMouseLeftButtonUp` to prevent `ArgumentNullException` crashes when retrieving coordinates/initiating snap animations on non-existent floating card instances.

---

## 5. Verification Plan

### Automated Tests
- Run `dotnet build` or `msbuild` to ensure clean compilation.

### Manual Verification
- Verify that a configuration with document type `&` generates pages correctly in EPLAN.
- Verify that placing multiple window macros on the same page applies variables independently without collision.
- Verify custom NCalc functions work via conditional checks.
- Verify that absolute placement positions macros at the correct coordinates.
- Verify that missing variable mappings fall back correctly to the variable's default value.
- Verify that unpopulated mandatory variables trigger an error rather than generating corrupt drawings.
