# Handoff Report — Empirical Challenger Review (M7)

## 1. Observation

- **Project Build Status**:
  - Command: `dotnet build`
  - Result: Compiled successfully with 0 warnings/errors.
- **Project Test Status**:
  - Command: `dotnet test`
  - Result: Failed! 1 test failed out of 61. Verbatim test output:
    ```
    [xUnit.net 00:00:01.59]     AutoSchematicConfigurator.EplAddIn.Tool.Tests.JsonDeserializationTests.Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend [FAIL]
      Failed AutoSchematicConfigurator.EplAddIn.Tool.Tests.JsonDeserializationTests.Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend [37 ms]
      Error Message:
       Assert.Equal() Failure: Strings differ
               ↓ (pos 0)
    Expected: "MY_FUNCTIONAL_ASSIGNMENT"
    Actual:   ""
      Stack Trace:
         at AutoSchematicConfigurator.EplAddIn.Tool.Tests.JsonDeserializationTests.Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend() in C:\Users\62789\source\repos\ASC.EplAddIn.Tool\tests\JsonDeserializationTests.cs:line 237

    Failed!  - Failed:     1, Passed:    60, Skipped:     0, Total:    61, Duration: 725 ms - AutoSchematicConfigurator.EplAddIn.Tool.Tests.dll (net481)
    ```

- **File Path**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorModel.cs`
  - Lines 80-84 (No replace attribute on `StructureMappings`):
    ```csharp
    public List<MappingEntry> StructureMappings { get; set; }
    public List<MappingEntry> VariableMappings { get; set; }

    public MacroElement()
    ```

- **File Path**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorService.Generator.cs`
  - Lines 197-210 (Child variable fallback evaluated using parent context):
    ```csharp
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
    ```

- **File Path**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorService.Evaluation.cs`
  - Lines 73-84 (Contains custom function is case-sensitive):
    ```csharp
                            else if (name.Equals("Contains", StringComparison.OrdinalIgnoreCase))
                            {
                                string str = args.Parameters[0].Evaluate()?.ToString() ?? "";
                                string sub = args.Parameters[1].Evaluate()?.ToString() ?? "";
                                args.Result = str.Contains(sub);
                            }
    ```

- **File Path**: `c:\Users\62789\source\repos\ASC.EplAddIn.Tool\ConfiguratorService.Generator.cs`
  - Line 313 (No null check on `AvailableStructureIdentifiers` list):
    ```csharp
    string functionalAssignment = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("==")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
    ```

---

## 2. Logic Chain

1. **Newtonsoft.Json List Duplication & Masking Bug**:
   - `MacroElement.StructureMappings` is initialized in the constructor with default items (e.g. `Key: "==", Value: ""`).
   - During JSON loading via Newtonsoft.Json, since `StructureMappings` lacks `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]`, deserialized items are appended to the list rather than replacing it.
   - As a result, the default mapping entries stay at the front of the list. When `FirstOrDefault` is called in the generation logic to fetch functional assignment or page name, it matches the first entry (the empty default) instead of the user's custom deserialized entry.
   - This is empirically proven by the failure of `Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend`.

2. **Child Variable Default Value Context Bug**:
   - In `GenerateTypicalGroup`, child variables that fall back to default value formulas are evaluated using the parent's `parameters` context rather than the child's parameters context.
   - If the formula references another child variable (which is not in the parent parameters), NCalc throws an evaluation exception and halts the generation. If the parent happens to have a variable with the same name, it evaluates using the parent's value instead of the child's default value.

3. **Contains custom function inconsistency**:
   - Custom functions `StartsWith` and `EndsWith` use `StringComparison.OrdinalIgnoreCase` (case-insensitive).
   - Custom function `Contains` uses C#'s standard `str.Contains(sub)` which is case-sensitive in .NET 4.8.1.
   - This leads to inconsistent and buggy behavior when users write formulas (e.g., `Contains(Cabinet, 'mcc')` evaluates to `false` when Cabinet is `"MCC"`).

4. **Potential NullReferenceException in Settings**:
   - If `db.Settings` is present but `AvailableStructureIdentifiers` is null (or deserialized as null), the call `db.Settings.AvailableStructureIdentifiers.Contains("==")` throws a `NullReferenceException` and crashes.

---

## 3. Caveats

- EPLAN-specific API calls are mocked during unit testing, so structural assignment integration (e.g. actually writing designations to EPLAN pages) was reviewed statically rather than dynamically in a live EPLAN instance.

---

## 4. Conclusion

**VERDICT: VETO**

The implementation is not robust and correct. The following changes are required from the worker:
1. Add `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]` to `StructureMappings` in `ConfiguratorModel.cs`.
2. Fix `GenerateTypicalGroup` so that child default value formulas are evaluated in the child's parameter context.
3. Update the custom `Contains` function in `ConfiguratorService.Evaluation.cs` to be case-insensitive (e.g. using `str.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0`).
4. Add null-checks to `db.Settings.AvailableStructureIdentifiers` before checking `.Contains()`.

---

## 5. Verification Method

To verify these findings and check subsequent fixes:
1. Run:
   ```powershell
   dotnet test
   ```
2. Verify that the test `Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend` (along with all other 60 tests) compiles and passes.
