# Test Infrastructure Specification

This document details the E2E and Unit testing infrastructure for the EPLAN Schematic Generator Add-In project.

## 1. Testing Framework and Environment
- **Framework**: xUnit (v2.6.2)
- **Target Framework**: .NET Framework 4.8.1 (`net481`)
- **Test Runner**: xUnit Visual Studio Runner (`xunit.runner.visualstudio` v2.5.4) / `dotnet test`
- **Platform Target**: `x64` (aligned with EPLAN Platform 64-bit runtime requirement)

## 2. Project Structure & Code Layout
All tests reside in the `tests/` directory at the project root.
```
ASC.EplAddIn.Tool/
├── tests/
│   ├── AutoSchematicConfigurator.EplAddIn.Tool.Tests.csproj
│   ├── PageNameFormattingTests.cs      # R1: Page naming and structure rules
│   ├── JsonDeserializationTests.cs     # R2: Newtonsoft.Json list duplication behavior
│   ├── PlaceholderMatchingTests.cs     # R3: Flexible placeholder mapping syntax
│   ├── ValidationAndFormulasTests.cs   # R4: Generation validation & NCalc expressions
│   └── IntegrationAndWorkloadTests.cs  # Tiers 3 & 4: Cross-feature and real-world scenarios
├── ConfiguratorService.cs              # Main service holding core logic and test stubs
└── Schematic AutoConfig.EplAddIn.Tool.slnx
```

## 3. Test Strategy and Licensing Isolation
EPLAN API assemblies (`Eplan.EplApi.Baseu.dll`, etc.) are runtime-dependent and require active EPLAN licensing. To allow tests to run on headless CI/CD systems and developers' environments without EPLAN licenses:
1. **Decoupling**: Business logic and formatting logic are decoupled into public helper methods in `ConfiguratorService`.
2. **Stubs**: EPLAN UI and EPLAN database transactions are isolated from the logical computation paths.
3. **Execution**: Standard unit/integration tests evaluate NCalc parser expressions, JSON deserialization behaviors, string formatting rules, mapping key matching, and variable validations.

## 4. Test Tiers
The test suite is structured into four distinct tiers:
- **Tier 1 (Basic/Standard Unit Tests)**: Verifies correct behavior under standard operating conditions for each feature individually.
- **Tier 2 (Boundary/Edge Case Unit Tests)**: Checks behavior against extreme, empty, invalid, and null inputs.
- **Tier 3 (Cross-Feature Integration Tests)**: Exercises workflows involving interactions between multiple components (e.g. evaluating formulas, checking variable validation, and formatting page designations simultaneously).
- **Tier 4 (Real-World Application Workload Tests)**: Simulates complete, complex typical-group schematics generation setups (e.g., VFD/DOL motor control typicals with dependency chains and breaker lookups).

## 5. Running the Tests
Execute the following commands from the solution root directory:

```powershell
# Restore NuGet packages
dotnet restore

# Build the solution and test project
dotnet build

# Execute all tests in the solution
dotnet test
```

## 6. Verification Checklist
- At least 49 tests in total must be registered and detected.
- Build compiles without warnings.
- Stub tests must fail until the concrete implementations are finalized.
