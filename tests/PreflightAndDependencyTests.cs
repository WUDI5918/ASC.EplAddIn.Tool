using System;
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class PreflightAndDependencyTests
    {
        // ==================== P-8: variable dependency ordering ====================

        [Fact]
        public void SortVariablesByDependency_ShouldOrderDependentAfterReferenced()
        {
            var vars = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "B", DefaultValue = "=A" },
                new ConfigurationVariable { Name = "A", DefaultValue = "5" }
            };

            var sorted = ConfiguratorService.SortVariablesByDependency(vars);

            Assert.Equal("A", sorted[0].Name);
            Assert.Equal("B", sorted[1].Name);
        }

        [Fact]
        public void SortVariablesByDependency_WithCycle_ShouldReturnAllVariables()
        {
            var vars = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "A", DefaultValue = "=B" },
                new ConfigurationVariable { Name = "B", DefaultValue = "=A" }
            };

            var sorted = ConfiguratorService.SortVariablesByDependency(vars);

            Assert.Equal(2, sorted.Count);
        }

        // ==================== P-1: variant selection ====================

        [Fact]
        public void ResolveVariant_ShouldSelectMatchingVariant()
        {
            var variants = new List<VariantDefinition>
            {
                new VariantDefinition { Name = "A", Condition = "Power > 10" },
                new VariantDefinition { Name = "B", Condition = "Power <= 10" }
            };
            var parameters = new Dictionary<string, object> { { "Power", 15.0 } };

            var selected = ConfiguratorService.ResolveVariant(variants, parameters, out var matched);

            Assert.NotNull(selected);
            Assert.Equal("A", selected.Name);
            Assert.Single(matched);
        }

        [Fact]
        public void ResolveVariant_WithNoMatch_ShouldReturnNullAndEmptyMatches()
        {
            var variants = new List<VariantDefinition>
            {
                new VariantDefinition { Name = "A", Condition = "Power > 100" }
            };
            var parameters = new Dictionary<string, object> { { "Power", 15.0 } };

            var selected = ConfiguratorService.ResolveVariant(variants, parameters, out var matched);

            Assert.Null(selected);
            Assert.Empty(matched);
        }

        [Fact]
        public void ResolveVariant_WithMultipleMatches_ShouldReturnNullAndAllMatches()
        {
            var variants = new List<VariantDefinition>
            {
                new VariantDefinition { Name = "A", Condition = "Power > 0" },
                new VariantDefinition { Name = "B", Condition = "Power > 0" }
            };
            var parameters = new Dictionary<string, object> { { "Power", 15.0 } };

            var selected = ConfiguratorService.ResolveVariant(variants, parameters, out var matched);

            Assert.Null(selected);
            Assert.Equal(2, matched.Count);
        }

        // ==================== P-1: variant gating (t5) ====================

        [Fact]
        public void ResolveVariantGate_NoVariants_ShouldReturnNoVariants()
        {
            var gate = ConfiguratorService.ResolveVariantGate(null, new Dictionary<string, object>(), out var selected);
            Assert.Equal(VariantGateResult.NoVariants, gate);
            Assert.Null(selected);

            gate = ConfiguratorService.ResolveVariantGate(new List<VariantDefinition>(), null, out selected);
            Assert.Equal(VariantGateResult.NoVariants, gate);
            Assert.Null(selected);
        }

        [Fact]
        public void ResolveVariantGate_SingleMatch_ShouldReturnSelected()
        {
            var variants = new List<VariantDefinition>
            {
                new VariantDefinition { Name = "A", Condition = "Power > 10" },
                new VariantDefinition { Name = "B", Condition = "Power <= 10" }
            };
            var parameters = new Dictionary<string, object> { { "Power", 15.0 } };

            var gate = ConfiguratorService.ResolveVariantGate(variants, parameters, out var selected);

            Assert.Equal(VariantGateResult.Selected, gate);
            Assert.NotNull(selected);
            Assert.Equal("A", selected.Name);
        }

        [Fact]
        public void ResolveVariantGate_NoMatch_ShouldReturnNoMatch()
        {
            var variants = new List<VariantDefinition>
            {
                new VariantDefinition { Name = "A", Condition = "Power > 100" }
            };
            var parameters = new Dictionary<string, object> { { "Power", 15.0 } };

            var gate = ConfiguratorService.ResolveVariantGate(variants, parameters, out var selected);

            Assert.Equal(VariantGateResult.NoMatch, gate);
            Assert.Null(selected);
        }

        [Fact]
        public void ResolveVariantGate_MultipleMatches_ShouldReturnMultipleMatches()
        {
            var variants = new List<VariantDefinition>
            {
                new VariantDefinition { Name = "A", Condition = "Power > 0" },
                new VariantDefinition { Name = "B", Condition = "Power > 0" }
            };
            var parameters = new Dictionary<string, object> { { "Power", 15.0 } };

            var gate = ConfiguratorService.ResolveVariantGate(variants, parameters, out var selected);

            Assert.Equal(VariantGateResult.MultipleMatches, gate);
            Assert.Null(selected);
        }

        // ==================== P-3: pre-flight validation ====================

        [Fact]
        public void Preflight_WithMissingGroupRef_ShouldReportError()
        {
            var db = new ConfiguratorData();
            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("MissingGroup");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.True(report.HasErrors);
        }

        [Fact]
        public void Preflight_WithMissingMacroTypicalTarget_ShouldReportError()
        {
            var db = new ConfiguratorData();
            var group = new TypicalGroup { Name = "G1" };
            group.Instances.Add(new TypicalInstance { TargetName = "MissingMT", Type = "MacroTypical" });
            db.TypicalGroups.Add(group);

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.True(report.HasErrors);
        }

        [Fact]
        public void Preflight_WithCycle_ShouldReportError()
        {
            var db = new ConfiguratorData();
            var group = new TypicalGroup { Name = "G1" };
            group.Instances.Add(new TypicalInstance { TargetName = "G1", Type = "TypicalGroup" });
            db.TypicalGroups.Add(group);

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.True(report.HasErrors);
        }

        [Fact]
        public void Preflight_WithValidConfig_ShouldPass()
        {
            var db = new ConfiguratorData();
            var mt = new MacroTypical { Name = "MT1" };
            var group = new TypicalGroup { Name = "G1" };
            group.Instances.Add(new TypicalInstance { TargetName = "MT1", Type = "MacroTypical" });
            db.MacroTypicals.Add(mt);
            db.TypicalGroups.Add(group);

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var groupParameters = new Dictionary<string, Dictionary<string, object>>
            {
                { "G1", new Dictionary<string, object>() }
            };

            var report = PreflightValidator.ValidateConfigurator(db, cfg, groupParameters);

            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Preflight_WithInvalidInstanceFormula_ShouldReportError()
        {
            var db = new ConfiguratorData();
            var mt = new MacroTypical { Name = "MT1" };
            var group = new TypicalGroup { Name = "G1" };
            group.Instances.Add(new TypicalInstance { TargetName = "MT1", Type = "MacroTypical", IsActiveFormula = "invalid (((" });
            db.MacroTypicals.Add(mt);
            db.TypicalGroups.Add(group);

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.True(report.HasErrors);
        }

        // ==================== P-R: reference integrity (t3-H2 fix) ====================

        [Fact]
        public void Preflight_WithUndefinedVariableReference_ShouldReportWarning()
        {
            // Use_Cabinet is referenced by both the visibility formula and the instance
            // activation formula but is not defined in the group scope -> warning.
            var db = new ConfiguratorData();
            var group = new TypicalGroup { Name = "G1" };
            group.Variables.Add(new ConfigurationVariable { Name = "Cabinet_Current", Type = "String", VisibilityFormula = "=Use_Cabinet==true" });
            group.Instances.Add(new TypicalInstance { TargetName = "MT1", Type = "MacroTypical", IsActiveFormula = "=Use_Cabinet==true" });
            db.TypicalGroups.Add(group);
            db.MacroTypicals.Add(new MacroTypical { Name = "MT1" });

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.True(report.HasWarnings);
            Assert.Contains(report.Issues, i => i.Severity == PreflightSeverity.Warning && i.Message.Contains("Use_Cabinet"));
        }

        [Fact]
        public void Preflight_WithSiblingVariableReference_ShouldNotFlagIt()
        {
            // References to a variable that IS in the scope (a sibling) must not be reported.
            var db = new ConfiguratorData();
            var group = new TypicalGroup { Name = "G1" };
            group.Variables.Add(new ConfigurationVariable { Name = "Use_Cabinet", Type = "Boolean" });
            group.Variables.Add(new ConfigurationVariable { Name = "Cabinet_Current", Type = "String", VisibilityFormula = "=Use_Cabinet==true" });
            db.TypicalGroups.Add(group);

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("Use_Cabinet"));
            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Preflight_WithBuiltinFunctionsAndSystemVariables_ShouldNotFlagThem()
        {
            // System variables and built-in functions must never be reported as undefined variables.
            var db = new ConfiguratorData();
            var group = new TypicalGroup { Name = "G1" };
            group.Variables.Add(new ConfigurationVariable
            {
                Name = "Name",
                Type = "String",
                VisibilityFormula = "=IsAvailable('Power') && Contains(ProjectName, 'x') && User != ''"
            });
            db.TypicalGroups.Add(group);

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("G1");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.False(report.HasWarnings);
            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Preflight_WithSampleDataUseCabinetFix_ShouldPass()
        {
            // Mirrors the fixed configurator_data.json "Main Distribution Cabinet" group:
            // Use_Cabinet is defined, so the sample visibility/activation formulas no longer
            // produce undefined-variable warnings and pre-flight passes.
            var db = new ConfiguratorData();
            var group = new TypicalGroup { Name = "Main Distribution Cabinet" };
            group.Variables.Add(new ConfigurationVariable { Name = "Function_designation", Type = "String" });
            group.Variables.Add(new ConfigurationVariable { Name = "Use_Cabinet", Type = "Boolean" });
            group.Variables.Add(new ConfigurationVariable { Name = "Cabinet_Current", Type = "String", VisibilityFormula = "=Use_Cabinet==true" });
            group.Instances.Add(new TypicalInstance { TargetName = "MT1", Type = "MacroTypical", IsActiveFormula = "=Use_Cabinet==true" });
            db.TypicalGroups.Add(group);
            db.MacroTypicals.Add(new MacroTypical { Name = "MT1" });

            var cfg = new Configurator { Name = "C1" };
            cfg.TypicalGroupRefs.Add("Main Distribution Cabinet");

            var report = PreflightValidator.ValidateConfigurator(db, cfg, null);

            Assert.False(report.HasErrors);
            Assert.DoesNotContain(report.Issues, i => i.Message.Contains("Use_Cabinet"));
        }
    }
}
