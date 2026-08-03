# AutoSchematicConfigurator - System Specification

## 1. System Overview
AutoSchematicConfigurator is an advanced, rule-driven EPLAN automation add-in. Inspired by EPLAN Cogineer and eBuild, it eliminates hardcoded schematics generation logic by employing a generic rule configuration and parsing engine. Engineers define rules via a JSON structure, and the engine dynamically evaluates these rules to build projects, insert page/window macros, resolve locations, and replace placeholder variables.

## 2. Core Concepts (Cogineer Paradigm)
The data model divides schematics design into three distinct levels:
1. **Macro (EMA/EMP)**: The elementary graphic block containing circuits, placeholder objects, and predefined value sets.
2. **Macro-Typical**: A logical grouping of macros representing a functional circuit unit. It defines macro files, active conditions (logical formulas), location mapping, insert coordinates, and variable-to-placeholder assignments.
3. **Typical-Group / Configurator**: The high-level configuration profile shown to the user. It aggregates global user variables (inputs) and controls their propagation down to Macro-Typicals.

---

## 3. System Architecture
The plugin is built using C# and WPF under the MVVM pattern, executing within the EPLAN process space.

```mermaid
graph TD
    UI[WPF UI - Ribbon & Window] --> VM[ViewModels]
    VM --> Service[ConfiguratorService]
    Service --> Engine[RulesEngine - NCalc]
    Service --> Wrapper[EPLAN API Wrapper]
    Engine --> Lookup[SQLite / CSV lookup]
    Wrapper --> EPLAN[EPLAN Platform APIs]
```

### 3.1 Expression Evaluation Rules
Rules and value mapping expressions utilize the NCalc expression library. The expression engine must resolve:
- **Logical Expressions**: `MotorType == 'Direct' && Power_kW <= 15`
- **Ternary Expressions**: `if(Power_kW > 11, '32A', '16A')`
- **String Manipulations**: `Concat([Power_kW], ' kW')`, `Concat('=', CabinetLocation, '+', Cabinet, '-M1')`
- **Mathematical Formulas**: `Round([Power_kW] * 2.0, 1)`, `Ceiling(Current * 1.25)`
- **External Lookups**: `Lookup_Breaker(Power_kW, MotorType)` which queries a SQLite selection database or CSV catalog file to return the exact Value Set name.

### 3.2 EPLAN API Interaction Wrappers
- **Window Macro (.ema)**: Inserted onto existing pages using coordinates.
- **Page Macro (.emp)**: Generates new pages containing page properties and frame designs.
- **Placeholder Value Sets**: Selects predefined value sets (`PlaceHolder.SelectValueSet(setName)`) and directly updates placeholder variables (`PlaceHolder.SetValue(var, val)`), followed by calling `PlaceHolder.Apply()` to redraw elements.
- **Execution Safety**: Execution is wrapped inside an EPLAN `Transaction` and `LockingStep`. Errors trigger automatic rollbacks. Progress is displayed using EPLAN's native `Progress` bar.

---

## 4. Business Workflow

### 4.1 Phase 1: Configuration Definition
1. **Macro Preparation**: Create `.ema`/`.emp` files with Placeholders and Value Sets in EPLAN.
2. **Rule Mapping**: Define Macro-Typical structure, position coordinates, visibility conditions, and parameter mapping rules in the JSON configuration database.
3. **Variable Exposure**: Define input fields (enums, numbers, strings) exposed in the typical group UI.

### 4.2 Phase 2: Execution & Generation
1. **Data Load**: Load UI inputs or external batch data (Excel/REST JSON).
2. **Expression Evaluation**: Parse typical rules using the Rules Engine. Inactive typicals are skipped.
3. **Coordinate & Page Logic**: Calculate coordinates. If target page does not exist, create it.
4. **Drawing Generation**: Initialize transaction, lock UI, insert macros, replace placeholders, apply value sets, and commit.
5. **Post-Process**: Refresh EPLAN project, trigger wire numbering/BOM generation if needed.

---

## 5. JSON Configuration Schema
```json
{
  "ConfiguratorName": "MotorControlCenter",
  "UserVariables": [
    { "Name": "MotorType", "Type": "Enum", "Options": ["Direct", "VFD"], "Default": "Direct" }
  ],
  "MacroTypicals": [
    {
      "Name": "Motor_Main_Circuit",
      "Condition": "MotorType == 'Direct' && Power_kW <= 15",
      "PageStructure": { "HigherLevel": "=MCC", "MountingLocation": "[CabinetLocation]", "PageType": "Circuit" },
      "MacrosToInsert": [
        {
          "MacroPath": "C:\\Macros\\DirectStart.ema",
          "X_Coordinate": "Auto_Next_X",
          "Y_Coordinate": 20.0,
          "PlaceholderValues": {
             "MotorPower": "Concat([Power_kW], ' kW')",
             "ValueSet_Selector": "Lookup_Breaker([Power_kW], 'Direct')"
          }
        }
      ]
    }
  ]
}
```

---

## 6. Verification and Acceptance Criteria
- **Syntax & Compilation**: Code must compile clean against EPLAN DLLs under C# (.NET Framework 4.8).
- **Rule Parser Accuracy**: Complex mathematical, string, and lookup expressions evaluate to correct outputs.
- **EPLAN Integration**: Ribbon registers menu item; WPF forms display properly; schematics pages are generated on-demand; transaction rolls back completely on execution error.

---

## 7. Production Usability & Compatibility Specifications

### 7.1 EPLAN Document Type (&) Support
- **Requirement**: The system must fully support EPLAN's standard Document Type (`&`) identifier in the page structure settings.
- **Acceptance Criteria**:
  - The UI mapping for "Document type [&]" must map to the `DESIGNATION_DOCTYPE` property in EPLAN instead of being converted to a user-defined `#` structure.
  - The target page name must format as `&DOCTYPE` in the project hierarchy representation.

### 7.2 Window Macro Placeholder Isolation
- **Requirement**: When placing multiple window macros on the same page, the configuration variables of the current macro element must NOT overwrite the placeholders of other macros already on that page.
- **Acceptance Criteria**:
  - The generator must filter the placeholders to process by comparing them against the array of newly inserted objects returned by `oInsert.WindowMacro`.
  - Placeholders from previously inserted macros must remain untouched.

### 7.3 Extended Engineering Formulas
- **Requirement**: The Rules Engine must support standard engineering helpers used in typical configurations.
- **Acceptance Criteria**:
  - The engine must support `IsAvailable(varName)`, `If(cond, true, false)`, `IsNullEmpty(str)`, `Contains(str, sub)`, `StartsWith(str, sub)`, and `EndsWith(str, sub)`.

### 7.4 Culture-Aware Variable Conversion & Default Fallbacks
- **Requirement**: The configuration values must be converted to their destination types (Boolean, Integer, Double) in a culture-invariant and casting-safe manner to prevent failures on systems with varying regional formats.
- **Requirement**: If a parent Typical Group does not define a mapping for a child variable, the generator must fall back to the child variable's `DefaultValue` rather than returning `null`/`false`.

### 7.5 Representation Type & Position Mode Overload Mapping
- **Requirement**: When inserting a Window Macro, the system must pass the macro element's `RepresentationType` (Multi-line, Single-line, Overview, etc.) to EPLAN to ensure the correct drawing style is selected.
- **Requirement**: If the position mode is set to `"Absolute"`, the system must use EPLAN's `Insert.MoveKind.Absolute` mode to place the macro at exact page coordinates.

### 7.6 Direct Placeholder Variable Mapping Compatibility
- **Requirement**: The placeholder application block must match mapping keys that correspond directly to the placeholder variable name (non-prefixed), in addition to prefixed keys (`PlaceholderName : VariableName`).

### 7.7 Mandatory Variable Generation-Time Validation
- **Requirement**: The generation engine must validate that all visible mandatory variables are populated, raising a descriptive exception to prevent generating corrupt schematics.

### 7.8 Drag/Resize Layout Jitter & Scrollbar Fix
- **Requirement**: Resizing or adjusting the navigator width must not trigger a layout loop, flickering horizontal scrollbars, or page jumping.
- **Acceptance Criteria**:
  - The Macros DataGrid must have its horizontal scrollbar explicitly disabled (`HorizontalScrollBarVisibility="Disabled"`).
  - Character ellipsis text trimming must be utilized for columns containing long text (such as file paths) to ensure smooth scaling without requiring horizontal scrolling.

### 7.9 Drag-Drop Reordering Null Safety
- **Requirement**: Releasing the mouse button during or after a drag-drop reordering gesture must not crash the application under any circumstances.
- **Acceptance Criteria**:
  - The mouse release handler (`General_PreviewMouseLeftButtonUp`) must safely check for null on the floating card (`_dragFloatingCard == null`) and abort drag execution cleanly without throwing exceptions.

### 7.10 Column Header Separators Removal
- **Requirement**: Column headers of the Macros DataGrid must not show vertical divider/separator lines, matching a cleaner, modern layout.
- **Acceptance Criteria**:
  - The custom DataGrid `ColumnHeaderStyle` must collapse/hide vertical separators (`SeparatorVisibility="Collapsed"`, `SeparatorBrush="Transparent"`).
