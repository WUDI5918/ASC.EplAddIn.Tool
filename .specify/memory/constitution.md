# AutoSchematicConfigurator Constitution

## Core Principles

### I. Real and Executable Operations (No Simulation)
All drawing, routing, evaluation, and value mapping operations must be real and executable. No placeholder simulation or dummy behavior is allowed in production code. The rules engine must use actual expression parsers (NCalc), and EPLAN actions must interact with EPLAN Platform APIs directly.

### II. Strict Layer Decoupling (Cogineer Paradigm)
1. **Model Layer**: JSON rules files describing user variables, MacroTypicals, and TypicalGroups.
2. **Rules Engine Layer**: Handles conditional evaluation, coordinate math, string manipulation, and lookup table queries without depending on EPLAN APIs.
3. **EPLAN API Wrapper Layer**: Encapsulates EPLAN's native API transactions (`Transaction`, `LockingStep`, `Progress`, macro insertions, page generations, and placeholder updating). Decoupled from direct UI or data integration logic.
4. **UI Layer**: WPF MVVM for rule design and execution dynamic forms, running on the EPLAN main thread thread-safely.

### III. Data Source Agnosticism
Data Integration must support standard exchange formats (CSV/Excel via EPPlus/NPOI, REST JSON API) without hardcoding business rules.

### IV. Transaction Safety & Progress Reporting
Every batch schematics generation process must run under a single database Transaction. Any error must trigger an automatic roll back to protect project database integrity. Long-running processes must report status via EPLAN's native `Progress` bar and disable GUI redraw to maximize performance.

## Technology Stack Constraints
- **Target Framework**: .NET Framework 4.8 (compatible with EPLAN Platform 2026.0.3).
- **Expression Evaluation**: NCalc library.
- **Serialization**: System.Text.Json or Newtonsoft.Json (replacing legacy JavaScriptSerializer).
- **External Data**: EPPlus for Excel processing, HttpClient for REST.
- **EPLAN APIs**: `Eplan.EplApi.Baseu`, `Eplan.EplApi.DataModelu`, `Eplan.EplApi.HEServicesu`, `Eplan.EplApi.Guiu`, etc.

## Governance
- The code must compile using `dotnet build` or `msbuild`.
- Changes must verify against real EPLAN API usage and testable rules.

**Version**: 1.0.0 | **Ratified**: 2026-06-25 | **Last Amended**: 2026-06-25
