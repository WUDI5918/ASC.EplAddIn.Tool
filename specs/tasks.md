# AutoSchematicConfigurator Task Checklist

- [ ] Project Setup & Solution Fixes
  - [ ] Fix solution mapping in `AutoSchematicConfigurator.EplAddIn.Tool.slnx`
  - [ ] Update assembly metadata and add NuGet PackageReferences in `AutoSchematicConfigurator.EplAddIn.Tool.csproj`
  - [ ] Restore and verify NuGet packages

- [ ] Namespace & Class Renaming
  - [ ] Rename namespaces to `AutoSchematicConfigurator.EplAddIn.Tool` in all C# source files
  - [ ] Update Ribbon Command Action registration from `TestAction` to `AutoSchematicConfiguratorAction`
  - [ ] Update x:Class in WPF `ConfiguratorWindow.xaml` and code-behind
  - [ ] Fix hardcoded json database file paths in the code-behind to point to `c:\Users\62789\source\repos\ASC.EplAddIn.Tool`

- [ ] Rule Engine Upgrade (NCalc)
  - [ ] Write evaluation service using NCalc
  - [ ] Implement custom functions: `Concat(...)` and `Lookup_Breaker(...)`
  - [ ] Replace simple mock condition checks in `ConfiguratorService.cs` with the new NCalc evaluator

- [ ] EPLAN API Wrapper & Robustness
  - [ ] Refine page creation and location structural routing
  - [ ] Update Placeholder variable mapping and Value Set activation logic (`PlaceHolder.Apply()`)
  - [ ] Wrap generation logic inside an EPLAN transaction with safety lock and progress indicators

- [ ] Verification & Build
  - [ ] Run `dotnet build` to ensure clean compilation
  - [ ] Create walkthrough documentation
