# Handoff Report — Challenger 2 Review (M7)

## 1. Observation

- **Modified File**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorModel.cs`
  - *Observation*: `MacroElement.StructureMappings` is initialized in the constructor (lines 92-99):
    ```csharp
    StructureMappings = new List<MappingEntry>
    {
        new MappingEntry("==", ""), // Functional assignment
        new MappingEntry("=", ""),  // Function designation
        new MappingEntry("+", ""),  // Location designation
        new MappingEntry("UserDefined", ""),
        new MappingEntry("PageName", "101")
    };
    ```
    However, the property `StructureMappings` does **not** have the `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]` attribute.

- **Source Code**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorService.Eplan.cs`
  - *Observation*: The method `GetOrCreatePage` queries structure mappings using `FirstOrDefault` (lines 27-32):
    ```csharp
    string functionalAssignment = (settings == null || settings.AvailableStructureIdentifiers.Contains("==")) ? (structures.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
    string functionDesignation = (settings == null || settings.AvailableStructureIdentifiers.Contains("=")) ? (structures.FirstOrDefault(s => s.Key == "=")?.Value ?? "") : "";
    string locationDesignation = (settings == null || settings.AvailableStructureIdentifiers.Contains("+")) ? (structures.FirstOrDefault(s => s.Key == "+")?.Value ?? "") : "";
    string documentType = (settings == null || settings.AvailableStructureIdentifiers.Contains("&")) ? (structures.FirstOrDefault(s => s.Key == "&")?.Value ?? "") : "";
    string userDefined = (settings == null || settings.AvailableStructureIdentifiers.Contains("UserDefined") || settings.AvailableStructureIdentifiers.Contains("#")) ? (structures.FirstOrDefault(s => s.Key == "UserDefined" || s.Key == "#")?.Value ?? "") : "";
    string pageName = (settings == null || settings.AvailableStructureIdentifiers.Contains("PageName")) ? (structures.FirstOrDefault(s => s.Key == "PageName")?.Value ?? "") : "";
    ```

- **Source Code**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorService.Evaluation.cs`
  - *Observation*: Method `EvaluateValueFormula` implements fallback behavior on expression failure (lines 160-164):
    ```csharp
    catch (Exception ex)
    {
        Log("Warning", $"值计算公式评估异常 '{formula}': {ex.Message}，回退使用原始公式字符串");
        return formula; // Fallback to raw string
    }
    ```

- **Source Code**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\GenerationLogWindow.xaml.cs`
  - *Observation*: The window has no handler for the `Closing` event, and the generation block is launched synchronously on the UI thread via `DoEvents` (lines 80-108). The title bar 'X' button remains active.

- **Test Execution**: `dotnet test`
  - *Observation*: Added the test case `Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend` to `tests/JsonDeserializationTests.cs`. Running `dotnet test` failed with:
    ```
    Failed AutoSchematicConfigurator.EplAddIn.Tool.Tests.JsonDeserializationTests.Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend [27 ms]
      Error Message:
       Assert.Equal() Failure: Strings differ
               ↓ (pos 0)
      Expected: "MY_FUNCTIONAL_ASSIGNMENT"
      Actual:   ""
      Stack Trace:
         at AutoSchematicConfigurator.EplAddIn.Tool.Tests.JsonDeserializationTests.Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend() in C:\Users\62789\source\repos\ASC.EplAddIn.Tool\tests\JsonDeserializationTests.cs:line 237
    ```

---

## 2. Logic Chain

1. **JSON List Duplication**:
   - NewtonSoft.Json defaults to `ObjectCreationHandling.Auto` for list deserialization, which appends elements to pre-existing list instances initialized by object constructors.
   - Since `MacroElement.StructureMappings` is pre-populated with 5 default entries (all having empty values except `PageName` which is `"101"`), deserialization of custom structure mappings from JSON appends the custom mappings to the list (resulting in 7+ entries).
   - In `GetOrCreatePage`, structure values are fetched using `.FirstOrDefault(s => s.Key == "...")`.
   - The first match for keys like `==` or `PageName` in the list is always the constructor's default empty/default value (which sits at index 0..4).
   - Consequently, **any custom structure mappings configured in `configurator_data.json` are completely ignored**, causing page creation to fall back to blank functional designations and default pages. This is confirmed by the failing test case.

2. **NullReferenceException Risk**:
   - If settings are loaded but `AvailableStructureIdentifiers` is deserialized as `null`, evaluating `settings.AvailableStructureIdentifiers.Contains(...)` in `GetOrCreatePage` will throw a `NullReferenceException`.

3. **Formula Error Fallback Corruption**:
   - When a value formula (e.g. `=Power * 2`) fails to evaluate (e.g. because `Power` is missing or has a typo), `EvaluateValueFormula` returns the raw string `=Power * 2`.
   - Returning the raw formula string causes EPLAN to set the page name or placeholder values literally to `=Power * 2`, writing formula syntax directly into schematics.

4. **UI Thread Thread-Safety / Race Conditions**:
   - Since `GenerationLogWindow` is a modal dialog but does not override or block the `Closing` event, users can click the "X" title bar button while `ExecuteGeneration()` is running.
   - If the window closes mid-generation, the EPLAN transaction continues, but log events or status updates attempting to use the Dispatcher on a closed window will trigger exceptions.

---

## 3. Caveats

- **EPLAN API Availability**: Testing is restricted to decoupled static logic helpers since the EPLAN API cannot be instantiated outside the EPLAN runtime process.
- **Breaker Catalog Sorting**: The breaker catalog lookup assumes the catalog CSV is pre-sorted in ascending order. If it is unsorted, it returns incorrect breaker models.

---

## 4. Conclusion

**VERDICT: VETO**

The implementation contains a critical bug: `MacroElement.StructureMappings` undergoes list duplication during JSON deserialization, which causes all custom EPLAN structure designations (like functional assignments, location codes, and custom page numbers) configured in `configurator_data.json` to be completely ignored.

To resolve this, the implementer must add `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]` to the `StructureMappings` property in `ConfiguratorModel.cs`, similar to `SelectedLanguages` and `AvailableStructureIdentifiers`.

---

## 5. Verification Method

To verify this defect:
1. Run `dotnet test` from the root directory.
2. The test suite will fail on `Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend` with expected vs actual mismatch due to the list duplication.
