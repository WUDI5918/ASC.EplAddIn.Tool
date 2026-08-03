# Test Ready Sign-Off

This document serves as the sign-off for the test suite initialization. It outlines the checklist of features, test coverage, and execution instructions.

## 1. Feature Checklist & Test Coverage

| ID | Feature | Tier 1 (Standard) | Tier 2 (Edge/Boundary) | T3 (Cross-Feature) | T4 (Real-World) |
|---|---|---|---|---|---|
| **R1** | **Page Structure Designation Support** | 7 tests in `PageNameFormattingTests` | 6 tests in `PageNameFormattingTests` | Covered in `IntegrationAndWorkloadTests` | Covered in `IntegrationAndWorkloadTests` |
| **R2** | **JSON Settings Deserialization List Duplication** | 6 tests in `JsonDeserializationTests` | 6 tests in `JsonDeserializationTests` | Covered in `IntegrationAndWorkloadTests` | Covered in `IntegrationAndWorkloadTests` |
| **R3** | **Flexible Placeholder Name Matching** | 6 tests in `PlaceholderMatchingTests` | 6 tests in `PlaceholderMatchingTests` | Covered in `IntegrationAndWorkloadTests` | Covered in `IntegrationAndWorkloadTests` |
| **R4** | **Generation-time Validation & WPF Status** | 6 tests in `ValidationAndFormulasTests` | 6 tests in `ValidationAndFormulasTests` | Covered in `IntegrationAndWorkloadTests` | Covered in `IntegrationAndWorkloadTests` |
| **All** | **Integration & Workloads (T3 & T4)** | - | - | 5 tests in `IntegrationAndWorkloadTests` | 6 tests in `IntegrationAndWorkloadTests` |

**Total Tests Created**: 60 (Requires at least 49 tests)

---

## 2. Test Cases Index

### 2.1 R1: Page Name Formatting (13 Tests)
1. `FormatPageName_WithAllIdentifiers_ShouldFormatCorrectly`
2. `FormatPageName_WithOnlyPageName_ShouldReturnPageNameWithSlash`
3. `FormatPageName_WithFunctionalAssignmentAndPageName_ShouldFormatCorrectly`
4. `FormatPageName_WithFunctionDesignationAndPageName_ShouldFormatCorrectly`
5. `FormatPageName_WithLocationDesignationAndPageName_ShouldFormatCorrectly`
6. `FormatPageName_WithDocumentTypeAndPageName_ShouldFormatCorrectly`
7. `FormatPageName_WithUserDefinedAndPageName_ShouldFormatCorrectly`
8. `FormatPageName_WithWhitespaceInputs_ShouldTrimCorrectly`
9. `FormatPageName_WithNullInputs_ShouldSkipMissingIdentifiers`
10. `FormatPageName_WithEmptyInputs_ShouldOnlyReturnSlashAndPageName`
11. `FormatPageName_WithSpecialCharactersInIdentifiers_ShouldPreserveSpecialCharacters`
12. `FormatPageName_WithLongPageName_ShouldFormatWithoutTruncation`
13. `FormatPageName_WithVaryingWhitespaceCases_ShouldTrimAllFields`

### 2.2 R2: JSON Deserialization (12 Tests)
1. `Deserialize_WithEmptyJson_ShouldReturnDefaultConfiguratorData`
2. `Deserialize_WithValidMacroTypical_ShouldParseCorrectly`
3. `Deserialize_WithLibrarySettingsUnit_ShouldParseCorrectly`
4. `Deserialize_WithConfigurator_ShouldParseNameAndTypicalGroups`
5. `Deserialize_WithVariables_ShouldParseTypeAndPredefinedValues`
6. `Deserialize_WithTypicalGroup_ShouldParseSubSegmentsAndInstances`
7. `Deserialize_SettingsSelectedLanguages_ShouldReplaceDefaultsNotAppend` (Expected Failure)
8. `Deserialize_SettingsAvailableStructureIdentifiers_ShouldReplaceDefaultsNotAppend` (Expected Failure)
9. `Deserialize_WithMalformedJson_ShouldThrowJsonException`
10. `Deserialize_WithMissingProperties_ShouldNotThrowAndHaveDefaults`
11. `Deserialize_WithExtraFields_ShouldIgnoreUnknownProperties`
12. `Deserialize_WithNullValuesInCollections_ShouldHandleGracefully`

### 2.3 R3: Placeholder Matching (12 Tests)
1. `FindPlaceholderMapping_ColonWithSpaces_ShouldMatch`
2. `FindPlaceholderMapping_EqualSign_ShouldMatch`
3. `FindPlaceholderMapping_OnlyVariableName_ShouldMatch`
4. `FindPlaceholderMapping_DotSeparator_ShouldMatch`
5. `FindPlaceholderMapping_ColonWithoutSpaces_ShouldMatch`
6. `FindPlaceholderMapping_MatchIsCaseInsensitive_ShouldMatch`
7. `FindPlaceholderMapping_NullMappings_ShouldReturnNull`
8. `FindPlaceholderMapping_EmptyMappingsList_ShouldReturnNull`
9. `FindPlaceholderMapping_MappingWithNoMatch_ShouldReturnNull`
10. `FindPlaceholderMapping_DuplicateMatchingKeys_ShouldReturnFirstMatch`
11. `FindPlaceholderMapping_WhitespaceKeys_ShouldHandleAndNotCrash`
12. `FindPlaceholderMapping_NullOrEmptyInputs_ShouldReturnNull`

### 2.4 R4: Validation & Formula Evaluation (12 Tests)
1. `ValidateVariables_WithMandatoryNullValue_ShouldThrowArgumentException` (Expected Failure)
2. `ValidateVariables_WithMandatoryEmptyValue_ShouldThrowArgumentException` (Expected Failure)
3. `ValidateVariables_WithMandatoryPopulatedValue_ShouldPass`
4. `EvaluateExpression_WithSimpleMath_ShouldEvaluate`
5. `EvaluateExpression_WithLogicalOperators_ShouldEvaluate`
6. `EvaluateExpression_WithStringConcat_ShouldEvaluate`
7. `ValidateVariables_WithVisibilityFormulaFalse_ShouldPassEvenIfNull`
8. `ValidateVariables_WithVisibilityFormulaTrue_ShouldThrowIfNull` (Expected Failure)
9. `EvaluateExpression_WithIfFormula_ShouldEvaluate`
10. `EvaluateExpression_WithStartsWithEndsWith_ShouldEvaluate`
11. `EvaluateExpression_WithContains_ShouldEvaluate`
12. `EvaluateExpression_WithNullEmptyCheck_ShouldEvaluate`

### 2.5 Tier 3: Cross-Feature Integration (5 Tests)
1. `CrossFeature_ValidateVariablesAndEvaluateExpressions_ShouldWorkTogether` (Expected Failure)
2. `CrossFeature_FormatPageNameWithEvaluatedFormulas_ShouldProduceCorrectName` (Expected Failure)
3. `CrossFeature_PlaceholderMappingWithEvaluatedFormulas_ShouldMatchAndResolve` (Expected Failure)
4. `CrossFeature_FullTypicalGroupSerializationAndValidation_ShouldDetectErrors` (Expected Failure)
5. `CrossFeature_BreakerLookupInFormulasAndValidation_ShouldQueryCatalogAndValidate`

### 2.6 Tier 4: Real-world Workloads (6 Tests)
1. `Workload_MotorTypicalSetup_ShouldPassValidationAndEvaluateBreakerAndPageStructure` (Expected Failure)
2. `Workload_PowerDistributionCabinet_ShouldHandleMultiplePlaceholdersAndFormatting` (Expected Failure)
3. `Workload_VariableDependencyChains_ShouldEvaluateInCorrectOrder` (Expected Failure)
4. `Workload_InvalidConfiguration_ShouldFailValidationAtGroupLevel` (Expected Failure)
5. `Workload_BulkPageMacroGeneration_ShouldBuildCorrectPageHierarchyStrings` (Expected Failure)
6. `Workload_UnusedVariablesAndVisibilityEdgeCases_ShouldNotThrow`

---

## 3. How to Run the Tests

To run the tests, execute the standard dotnet test tool command:
```powershell
dotnet test
```

### Expected Execution Behavior:
- The solution compiles successfully.
- Tests will run, and those asserting behaviors on helper methods (`FormatPageName`, `FindPlaceholderMapping`, `ValidateVariables`) will **fail** because they target the empty/stub definitions in `ConfiguratorService.cs`.
- Deserialization tests checking list duplication (`Deserialize_SettingsSelectedLanguages...` and `Deserialize_SettingsAvailableStructureIdentifiers...`) will **fail** because the `[JsonProperty]` attribute has not been added to the codebase.
- Tests asserting basic NCalc behavior (which is already implemented in `ConfiguratorService.Evaluation.cs`) will **pass**.

This is correct and verifies that our tests are active, genuine, and ready to assert behavior once the implementer completes the feature requirements.
