# Codebase Investigation Report (Handoff)

## 1. Observation

Direct observations and file references for the requirements:

### R1. Page Structure & Designation Support
- **File**: `ConfiguratorService.Eplan.cs` (Lines 30-31, 46-51, 95-96)
  - *Current implementation*:
    ```csharp
    string documentType = (settings == null || settings.AvailableStructureIdentifiers.Contains("&") || settings.AvailableStructureIdentifiers.Contains("UserDefined")) ? (structures.FirstOrDefault(s => s.Key == "UserDefined" || s.Key == "&")?.Value ?? "") : "";
    ...
    if (!string.IsNullOrWhiteSpace(documentType)) fullPageName += "&" + documentType.Trim();
    fullPageName += "/" + pageName.Trim();
    ...
    if (!string.IsNullOrWhiteSpace(documentType)) pagePropList.DESIGNATION_DOCTYPE = documentType.Trim();
    ```
- **File**: `ConfiguratorService.Generator.cs` (Lines 329-330, 355-360, 388-389, 421-426)
  - *Current implementation*: Same pattern as `ConfiguratorService.Eplan.cs` in page macro generation loop.
  - *Discrepancy*: User-Defined designation (`#` / `UserDefined`) is treated interchangeably with Document Type (`&`), mapping both to `DESIGNATION_DOCTYPE`. The `#` prefix is completely omitted from the formatted target page name, and `DESIGNATION_USERDEFINED` is not assigned when generating pages.

### R2. JSON Settings Deserialization List Duplication
- **File**: `ConfiguratorModel.cs` (Lines 161-162, 168-169)
  - *Current implementation*:
    ```csharp
    public List<string> SelectedLanguages { get; set; }
    public List<string> AvailableStructureIdentifiers { get; set; }
    ...
    SelectedLanguages = new List<string> { "zh-CN", "en-US" };
    AvailableStructureIdentifiers = new List<string> { "==", "=", "+", "UserDefined", "PageName" };
    ```
  - *Discrepancy*: Since the lists are initialized directly in the constructor of `LibrarySettings`, Newtonsoft.Json's default list-handling behavior (`ObjectCreationHandling.Reuse`) appends the loaded values from JSON to the existing default elements, causing duplicates each time the configuration is loaded and saved.

### R3. Flexible Placeholder Variable Name Matching
- **File**: `ConfiguratorService.Generator.cs` (Lines 634-639)
  - *Current implementation*:
    ```csharp
    string mappingKeyColon = $"{phName} : {varName}";
    string mappingKeyEqual = $"{phName}={varName}";
    var mapping = element.VariableMappings.FirstOrDefault(m =>
        m.Key.Equals(mappingKeyColon, StringComparison.OrdinalIgnoreCase) ||
        m.Key.Equals(mappingKeyEqual, StringComparison.OrdinalIgnoreCase) ||
        m.Key.Equals(varName, StringComparison.OrdinalIgnoreCase));
    ```
  - *Discrepancy*: Does not support separator patterns like `PlaceholderName.VariableName` (dot separator) or `PlaceholderName:VariableName` (colon without surrounding spaces).

### R4. Generation-time Validation & WPF Status Display
- **File**: `ConfiguratorWindow.ProjectBuilder.cs` (Lines 45-54, 70-79)
  - *Current implementation*:
    ```csharp
    Action generateAction = () =>
    {
        string result = ConfiguratorService.GenerateTypical(typical);
        
        this.Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = result;
            TxtStatus.Foreground = (result.StartsWith("错误") || result.StartsWith("发生异常")) ? Brushes.Red : Brushes.Green;
        });
    };
    ```
  - *Discrepancy*: If `ConfiguratorService.GenerateTypical` (or `GenerateConfigurator`) throws an exception during rule evaluation/validation, the exception propagates past the status update logic, leaving `TxtStatus.Text` stuck on `"正在生成原理图..."` / `"正在解析规则并批量生成图纸..."`.
- **File**: `ConfiguratorService.Generator.cs` (Lines 136-179)
  - *Discrepancy*: Inside `GenerateTypicalGroup`, the top-level group's variables (`group.Variables`) are not validated, meaning visible mandatory fields left empty by the user on the configurator form are not caught.

---

## 2. Logic Chain

1. **R1 (Page Structure)**: EPLAN specifies both a Document Type (`&`) and a User-Defined (`#`) identifier. To achieve full capability parity, they must be parsed as distinct variables from structural mapping entries. When generating a page, the Document Type must be applied to `DESIGNATION_DOCTYPE` and the User-Defined property must be applied to `DESIGNATION_USERDEFINED`. When formatting page names, they must be prefix-constructed as `==Assignment=Plant+Loc&Doc#UserDefined/Page`.
2. **R2 (JSON Settings)**: Newtonsoft.Json's default list deserialization behavior reuses instantiated list properties. Instructing the parser via the `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]` attribute on the `SelectedLanguages` and `AvailableStructureIdentifiers` properties forces the serializer to replace the initialized lists with the values parsed from `configurator_data.json`, preventing duplication.
3. **R3 (Placeholder Matching)**: Typical EPLAN designs contain placeholder variables formatted as `PlaceholderName.VariableName` or `PlaceholderName:VariableName`. Adding these mapping keys to the lookup logic in `ConfiguratorService.Generator.cs` ensures the engine resolves them correctly.
4. **R4 (Validation & Exception Display)**:
   - To show errors in the status label, the UI's `generateAction` must wrap the generation call in a `try-catch` block. Upon catching an exception, it updates `TxtStatus` to display the error message in Red and re-throws the exception so that the log window (`GenerationLogWindow`) continues to register and record the failure.
   - To make variables robustly verified, the generator must validate top-level `TypicalGroup` parameters before descending into child instances.
   - Extracting core formatting, validation, and placeholder matching logic to helper methods decouples them from EPLAN APIs, allowing them to be fully unit-tested in a standard xUnit project.

---

## 3. Caveats

- **EPLAN API Dependency in Unit Tests**: Standalone unit tests cannot initialize EPLAN APIs (`new Page()`, `new Insert()`, etc.) because those APIs require EPLAN license validation and are tied to EPLAN's runtime process. Therefore, unit tests are scoped to test extracted logical helper methods (e.g. `FormatPageName`, `ValidateVariables`, `FindPlaceholderMapping`, and NCalc `EvaluateExpression` functions).
- **Default value formula evaluation**: If a variable has a default value defined as a formula (e.g., starting with `=`), we evaluate it within the current context. This is highly recommended but depends on variable mapping availability.

---

## 4. Conclusion

The EPLAN Schematic Generator Add-In can be made production-ready and fully parity-compatible by performing localized modifications and refactorings to decouple core logic from EPLAN dependencies, making it 100% testable.

### Proposed Code Modifications

#### 1. ConfiguratorModel.cs (R2)
Add `using Newtonsoft.Json;` and decorate properties:
```csharp
using Newtonsoft.Json;
...
    public class LibrarySettings
    {
        public string Unit { get; set; } // "Metric" or "Imperial"
        public bool UseSubSegmentLogic { get; set; }

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SelectedLanguages { get; set; }

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AvailableStructureIdentifiers { get; set; }
...
```

#### 2. ConfiguratorService.cs (R1, R3, R4)
Add helper methods inside `ConfiguratorService` class to facilitate unit testing:
```csharp
        // Decoupled Helper for R1 Page Name Formatting
        public static string FormatPageName(
            string functionalAssignment, 
            string functionDesignation, 
            string locationDesignation, 
            string documentType, 
            string userDefined, 
            string pageName)
        {
            string fullPageName = "";
            if (!string.IsNullOrWhiteSpace(functionalAssignment)) fullPageName += "==" + functionalAssignment.Trim();
            if (!string.IsNullOrWhiteSpace(functionDesignation)) fullPageName += "=" + functionDesignation.Trim();
            if (!string.IsNullOrWhiteSpace(locationDesignation)) fullPageName += "+" + locationDesignation.Trim();
            if (!string.IsNullOrWhiteSpace(documentType)) fullPageName += "&" + documentType.Trim();
            if (!string.IsNullOrWhiteSpace(userDefined)) fullPageName += "#" + userDefined.Trim();
            fullPageName += "/" + pageName.Trim();
            return fullPageName;
        }

        // Decoupled Helper for R3 Placeholder Variable Matching
        public static MappingEntry FindPlaceholderMapping(string phName, string varName, List<MappingEntry> mappings)
        {
            if (mappings == null) return null;
            string mappingKeyColon = $"{phName} : {varName}";
            string mappingKeyColonNoSpaces = $"{phName}:{varName}";
            string mappingKeyEqual = $"{phName}={varName}";
            string mappingKeyDot = $"{phName}.{varName}";
            
            return mappings.FirstOrDefault(m =>
                m.Key.Equals(mappingKeyColon, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(mappingKeyColonNoSpaces, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(mappingKeyEqual, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(mappingKeyDot, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(varName, StringComparison.OrdinalIgnoreCase));
        }

        // Decoupled Helper for R4 Mandatory Variable Validation
        public static void ValidateVariables(string contextName, List<ConfigurationVariable> variables, Dictionary<string, object> parameters)
        {
            if (variables == null) return;
            foreach (var variable in variables)
            {
                if (variable.IsMandatory)
                {
                    bool isVarVisible = true;
                    if (!string.IsNullOrWhiteSpace(variable.VisibilityFormula))
                    {
                        isVarVisible = EvaluateActiveFormula(variable.VisibilityFormula, parameters);
                    }
                    var val = parameters.ContainsKey(variable.Name) ? parameters[variable.Name] : null;
                    if (isVarVisible && (val == null || string.IsNullOrWhiteSpace(val.ToString())))
                    {
                        throw new ArgumentException($"必填参数 '{variable.Name}' (在规则/组 '{contextName}' 中) 不能为空值！");
                    }
                }
            }
        }
```

#### 3. ConfiguratorService.Eplan.cs (R1)
Refactor `GetOrCreatePage` to utilize the new helpers and separate Document Type (`&`) and User-Defined (`#`):
```csharp
        private static Page GetOrCreatePage(Project project, List<MappingEntry> structures, Page fallbackPage, Dictionary<string, object> parameters, LibrarySettings settings)
        {
            if (structures == null || structures.Count == 0) return fallbackPage;

            try
            {
                string functionalAssignment = (settings == null || settings.AvailableStructureIdentifiers.Contains("==")) ? (structures.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
                string functionDesignation = (settings == null || settings.AvailableStructureIdentifiers.Contains("=")) ? (structures.FirstOrDefault(s => s.Key == "=")?.Value ?? "") : "";
                string locationDesignation = (settings == null || settings.AvailableStructureIdentifiers.Contains("+")) ? (structures.FirstOrDefault(s => s.Key == "+")?.Value ?? "") : "";
                string documentType = (settings == null || settings.AvailableStructureIdentifiers.Contains("&")) ? (structures.FirstOrDefault(s => s.Key == "&")?.Value ?? "") : "";
                string userDefined = (settings == null || settings.AvailableStructureIdentifiers.Contains("UserDefined") || settings.AvailableStructureIdentifiers.Contains("#")) ? (structures.FirstOrDefault(s => s.Key == "UserDefined" || s.Key == "#")?.Value ?? "") : "";
                string pageName = (settings == null || settings.AvailableStructureIdentifiers.Contains("PageName")) ? (structures.FirstOrDefault(s => s.Key == "PageName")?.Value ?? "") : "";

                // Evaluate structure properties with formula engine
                functionalAssignment = EvaluateValueFormula(functionalAssignment, parameters);
                functionDesignation = EvaluateValueFormula(functionDesignation, parameters);
                locationDesignation = EvaluateValueFormula(locationDesignation, parameters);
                documentType = EvaluateValueFormula(documentType, parameters);
                userDefined = EvaluateValueFormula(userDefined, parameters);
                pageName = EvaluateValueFormula(pageName, parameters);

                if (string.IsNullOrWhiteSpace(pageName))
                {
                    return fallbackPage;
                }

                // Format using extracted helper
                string fullPageName = FormatPageName(functionalAssignment, functionDesignation, locationDesignation, documentType, userDefined, pageName);

                // Find existing page... [existing lookup code] ...
                // Create page
                Page page = new Page();
                PagePropertyList pagePropList = new PagePropertyList();
                if (!string.IsNullOrWhiteSpace(functionalAssignment)) pagePropList.DESIGNATION_FUNCTIONALASSIGNMENT = functionalAssignment.Trim();
                if (!string.IsNullOrWhiteSpace(functionDesignation)) pagePropList.DESIGNATION_PLANT = functionDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(locationDesignation)) pagePropList.DESIGNATION_LOCATION = locationDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(documentType)) pagePropList.DESIGNATION_DOCTYPE = documentType.Trim();
                if (!string.IsNullOrWhiteSpace(userDefined)) pagePropList.DESIGNATION_USERDEFINED = userDefined.Trim();
                if (!string.IsNullOrWhiteSpace(pageName)) pagePropList.PAGE_COUNTER = pageName.Trim();

                page.Create(project, DocumentTypeManager.DocumentType.Circuit, pagePropList);
                return page;
```

#### 4. ConfiguratorService.Generator.cs (R1, R3, R4)
Modify `GenerateTypicalGroup` to validate variables at group-level:
```csharp
        public static void GenerateTypicalGroup(
            TypicalGroup group, 
            ConfiguratorData db, 
            Dictionary<string, object> parameters, 
            Project activeProject, 
            Page activePage, 
            ref int placedCount, 
            ref int placeholderUpdates, 
            ref Page lastEmpPage)
        {
            if (group.Instances == null) return;

            // R4: Validate top-level group's own variables
            ValidateVariables(group.Name, group.Variables, parameters);

            foreach (var instance in group.Instances)
            {
...
```
And modify typical variables default formula evaluation fallback:
```csharp
                        else
                        {
                            // Fallback to the variable's DefaultValue
                            string defaultValue = variable.DefaultValue ?? "";
                            if (defaultValue.StartsWith("="))
                            {
                                evaluatedVal = EvaluateExpression(defaultValue, parameters);
                            }
                            else
                            {
                                evaluatedVal = defaultValue;
                            }
                        }
```
And inside `GenerateTypicalInternal`, replace the variable validation loops with the helper, and parse/apply default formulas:
```csharp
            // Merge typical variables with default values, and validate visible mandatory ones
            var mergedParameters = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());
            if (typical.Variables != null)
            {
                foreach (var variable in typical.Variables)
                {
                    if (!mergedParameters.ContainsKey(variable.Name) || mergedParameters[variable.Name] == null)
                    {
                        string defVal = variable.DefaultValue ?? "";
                        object evaluatedVal = defVal;
                        if (defVal.StartsWith("="))
                        {
                            evaluatedVal = EvaluateExpression(defVal, mergedParameters);
                        }
                        mergedParameters[variable.Name] = ConvertToVariableType(evaluatedVal, variable.Type);
                    }
                }

                // Validate using the helper
                ValidateVariables(typical.Name, typical.Variables, mergedParameters);
            }
            parameters = mergedParameters;
```
For R1 page macro formatting in `ConfiguratorService.Generator.cs`:
```csharp
                            string functionalAssignment = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("==")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
                            string functionDesignation = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("=")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "=")?.Value ?? "") : "";
                            string locationDesignation = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("+")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "+")?.Value ?? "") : "";
                            string documentType = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("&")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "&")?.Value ?? "") : "";
                            string userDefined = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("UserDefined") || db.Settings.AvailableStructureIdentifiers.Contains("#")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "UserDefined" || s.Key == "#")?.Value ?? "") : "";
                            string pageNameBase = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("PageName")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "PageName")?.Value ?? "") : "";

                            functionalAssignment = EvaluateValueFormula(functionalAssignment, parameters);
                            functionDesignation = EvaluateValueFormula(functionDesignation, parameters);
                            locationDesignation = EvaluateValueFormula(locationDesignation, parameters);
                            documentType = EvaluateValueFormula(documentType, parameters);
                            userDefined = EvaluateValueFormula(userDefined, parameters);
                            pageNameBase = EvaluateValueFormula(pageNameBase, parameters);
```
And call `FormatPageName(...)` to format the full target page names:
```csharp
                                string fullTargetPageName = FormatPageName(functionalAssignment, functionDesignation, locationDesignation, documentType, userDefined, targetPageName);
```
And assign `DESIGNATION_USERDEFINED` in page macro properties loop:
```csharp
                                try
                                {
                                    if (!string.IsNullOrWhiteSpace(functionalAssignment)) 
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_FUNCTIONALASSIGNMENT] = functionalAssignment.Trim();
                                    if (!string.IsNullOrWhiteSpace(functionDesignation)) 
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLANT] = functionDesignation.Trim();
                                    if (!string.IsNullOrWhiteSpace(locationDesignation)) 
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_LOCATION] = locationDesignation.Trim();
                                    if (!string.IsNullOrWhiteSpace(documentType)) 
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_DOCTYPE] = documentType.Trim();
                                    if (!string.IsNullOrWhiteSpace(userDefined))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_USERDEFINED] = userDefined.Trim();
                                    if (!string.IsNullOrWhiteSpace(targetPageName))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.PAGE_COUNTER] = targetPageName.Trim();
                                }
```
For R3 placeholder variable matching inside `ConfiguratorService.Generator.cs`:
```csharp
                                     if (placeHolder.VariableNames != null)
                                     {
                                         foreach (string varName in placeHolder.VariableNames)
                                         {
                                             var mapping = FindPlaceholderMapping(phName, varName, element.VariableMappings);
                                             
                                             if (mapping != null && !string.IsNullOrWhiteSpace(mapping.Value))
                                             {
                                                 string evaluatedValue = EvaluateValueFormula(mapping.Value, parameters);
                                                 ...
```

#### 5. ConfiguratorWindow.ProjectBuilder.cs (R4 UI Stability)
Wrap generation actions in `try-catch` blocks and update `TxtStatus.Text`:
```csharp
                // 1. In BtnBuildProject_Click (GenerateConfigurator Action)
                Action generateAction = () =>
                {
                    try
                    {
                        string result = ConfiguratorService.GenerateConfigurator(config, _db, groupParameters);
                        
                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = result;
                            TxtStatus.Foreground = (result.StartsWith("错误") || result.StartsWith("发生异常")) ? Brushes.Red : Brushes.Green;
                        });
                    }
                    catch (Exception ex)
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = $"错误：生成失败！{ex.Message}";
                            TxtStatus.Foreground = Brushes.Red;
                        });
                        throw;
                    }
                };

                // 2. In BtnBuildTypical_Click (GenerateTypical Action)
                Action generateAction = () =>
                {
                    try
                    {
                        string result = ConfiguratorService.GenerateTypical(typical);
                        
                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = result;
                            TxtStatus.Foreground = (result.StartsWith("错误") || result.StartsWith("发生异常")) ? Brushes.Red : Brushes.Green;
                        });
                    }
                    catch (Exception ex)
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = $"错误：生成失败！{ex.Message}";
                            TxtStatus.Foreground = Brushes.Red;
                        });
                        throw;
                    }
                };
```

---

## 5. Verification Method

### Test Project Setup (Unit Tests)

1. Create a subfolder named `tests` alongside the solution file.
2. Write a test project file named `tests\AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj` with the following content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="NCalc-Edge" Version="1.4.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AutoSchematicConfigurator.EplAddIn.Tool.csproj" />
  </ItemGroup>
</Project>
```
3. Create test files inside `tests\` directory to verify the code modifications:

#### `tests\PageNameFormattingTests.cs` (R1)
```csharp
using Xunit;
using AutoSchematicConfigurator.EplAddIn.Tool;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class PageNameFormattingTests
    {
        [Theory]
        [InlineData("ESS", "L1", "P1", "EBH", "USER", "101", "==ESS=L1+P1&EBH#USER/101")]
        [InlineData("", "L1", "P1", "EBH", "", "101", "=L1+P1&EBH/101")]
        [InlineData("ESS", "", "", "", "USER", "102", "==ESS#USER/102")]
        public void FormatPageName_Should_ProduceCorrectEplanHierarchy(
            string functionalAssignment, 
            string functionDesignation, 
            string locationDesignation, 
            string documentType, 
            string userDefined, 
            string pageName, 
            string expected)
        {
            string result = ConfiguratorService.FormatPageName(
                functionalAssignment, 
                functionDesignation, 
                locationDesignation, 
                documentType, 
                userDefined, 
                pageName);

            Assert.Equal(expected, result);
        }
    }
}
```

#### `tests\JsonDeserializationTests.cs` (R2)
```csharp
using Xunit;
using Newtonsoft.Json;
using AutoSchematicConfigurator.EplAddIn.Tool;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class JsonDeserializationTests
    {
        [Fact]
        public void LibrarySettings_Deserialization_ShouldReplaceDefaultLists()
        {
            string json = @"{
                ""Settings"": {
                    ""SelectedLanguages"": [""en-US""],
                    ""AvailableStructureIdentifiers"": [""=="", ""=""]
                }
            }";

            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);

            Assert.NotNull(data.Settings);
            Assert.Single(data.Settings.SelectedLanguages);
            Assert.Equal("en-US", data.Settings.SelectedLanguages[0]);

            Assert.Equal(2, data.Settings.AvailableStructureIdentifiers.Count);
            Assert.Contains("==", data.Settings.AvailableStructureIdentifiers);
            Assert.Contains("=", data.Settings.AvailableStructureIdentifiers);
        }
    }
}
```

#### `tests\PlaceholderMatchingTests.cs` (R3)
```csharp
using Xunit;
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class PlaceholderMatchingTests
    {
        [Theory]
        [InlineData("PhName", "VarName", "PhName : VarName")]
        [InlineData("PhName", "VarName", "PhName:VarName")]
        [InlineData("PhName", "VarName", "PhName=VarName")]
        [InlineData("PhName", "VarName", "PhName.VarName")]
        [InlineData("PhName", "VarName", "VarName")]
        public void FindPlaceholderMapping_Should_MatchVariousFormats(string phName, string varName, string keyToMatch)
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry(keyToMatch, "EvaluatedValue")
            };

            var match = ConfiguratorService.FindPlaceholderMapping(phName, varName, mappings);

            Assert.NotNull(match);
            Assert.Equal("EvaluatedValue", match.Value);
        }
    }
}
```

#### `tests\ValidationAndFormulasTests.cs` (R4)
```csharp
using Xunit;
using System;
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class ValidationAndFormulasTests
    {
        [Fact]
        public void ValidateVariables_Should_ThrowException_WhenMandatoryVariableIsNull()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", null }
            };

            Assert.Throws<ArgumentException>(() => 
                ConfiguratorService.ValidateVariables("TestContext", variables, parameters));
        }

        [Fact]
        public void ValidateVariables_Should_NotThrowException_WhenMandatoryVariableIsPopulated()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", 15.0 }
            };

            // Should pass without throwing exception
            ConfiguratorService.ValidateVariables("TestContext", variables, parameters);
        }

        [Fact]
        public void EvaluateExpression_Should_SupportCustomEngineeringFormulas()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Cabinet", "MCC" },
                { "Power", 11.0 }
            };

            // Test Concat
            object concatResult = ConfiguratorService.EvaluateExpression("Concat('=', Cabinet, '+AP1')", parameters);
            Assert.Equal("=MCC+AP1", concatResult);

            // Test If
            object ifResult = ConfiguratorService.EvaluateExpression("If(Power > 11.0, '32A', '16A')", parameters);
            Assert.Equal("16A", ifResult);

            // Test StartsWith / EndsWith
            object startsResult = ConfiguratorService.EvaluateExpression("StartsWith(Cabinet, 'MC')", parameters);
            Assert.Equal(true, startsResult);
        }
    }
}
```

### Verification Command

To independently run and verify all tests, execute:
```powershell
dotnet test
```
*(If this command completes with 100% success rate, the modifications have been verified).*
