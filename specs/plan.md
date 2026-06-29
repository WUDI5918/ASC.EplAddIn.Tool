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

---

## 5. Verification Plan

### Automated Tests
- Run `dotnet build` to ensure the project builds successfully with NuGet packages restored.

### Manual Verification
- Verify the WPF UI loads.
- Test the expression engine with boolean, ternary, string concat, and lookup expressions.
