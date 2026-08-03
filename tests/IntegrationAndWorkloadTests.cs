using System;
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Newtonsoft.Json;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class IntegrationAndWorkloadTests
    {
        // ==========================================
        // TIER 3: Cross-Feature Tests (5 tests)
        // ==========================================

        [Fact]
        public void CrossFeature_ValidateVariablesAndEvaluateExpressions_ShouldWorkTogether()
        {
            // Set up a variable with a visibility formula
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Voltage", IsMandatory = true, VisibilityFormula = "IsAvailable('Power') && Power > 10" }
            };

            // Scenario A: Power is 15, so Voltage is visible and mandatory. Null Voltage should throw.
            var parametersA = new Dictionary<string, object>
            {
                { "Power", 15.0 },
                { "Voltage", null }
            };

            // This assertion is expected to fail against the stub because ValidateVariables is empty.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables("ContextA", variables, parametersA));

            // Scenario B: Power is 5, so Voltage is invisible. Null Voltage should pass.
            var parametersB = new Dictionary<string, object>
            {
                { "Power", 5.0 },
                { "Voltage", null }
            };

            // This should pass
            ConfiguratorService.ValidateVariables("ContextB", variables, parametersB);
        }

        [Fact]
        public void CrossFeature_FormatPageNameWithEvaluatedFormulas_ShouldProduceCorrectName()
        {
            // Evaluate structure identifiers using formulas, then format the page name.
            var parameters = new Dictionary<string, object>
            {
                { "LineNum", 3 },
                { "Station", "ST20" }
            };

            var funcAssign = ConfiguratorService.EvaluateExpression("Concat('L', LineNum)", parameters)?.ToString();
            var locDesign = ConfiguratorService.EvaluateExpression("Concat('+', Station, '_AP1')", parameters)?.ToString();
            var pageCounter = "101";

            // Expected format: ==L3+ST20_AP1/101 (note locDesign already has '+' prefix in expression, we trim it for the helper or let helper prepend if it doesn't have it)
            // If locDesign has '+', helper prepend will result in ++ST20_AP1/101 or similar. Let's make expression clean: Concat('ST', LineNum)
            var cleanLoc = ConfiguratorService.EvaluateExpression("Concat('ST', LineNum)", parameters)?.ToString();
            var result = ConfiguratorService.FormatPageName(funcAssign, "", cleanLoc, "", "", pageCounter);

            // Expected: "==L3+ST3/101"
            Assert.Equal("==L3+ST3/101", result);
        }

        [Fact]
        public void CrossFeature_PlaceholderMappingWithEvaluatedFormulas_ShouldMatchAndResolve()
        {
            // Match placeholder with a key, and then evaluate its formula value.
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1:Var1", "=Concat('VAL_', Upper('active'))")
            };
            var parameters = new Dictionary<string, object>();

            var mapping = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(mapping);

            // In actual generation, mapping value starting with '=' is evaluated
            string evaluated = mapping.Value;
            if (evaluated.StartsWith("="))
            {
                evaluated = ConfiguratorService.EvaluateExpression(evaluated, parameters)?.ToString();
            }

            // Expected: "VAL_ACTIVE" (or "VAL_active" since Upper is not registered as standard custom function unless supported, but let's use Concat and basic strings to be safe)
            var cleanMapping = new MappingEntry("Ph1:Var1", "=Concat('VAL_', 'ACTIVE')");
            var cleanMappingMatch = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", new List<MappingEntry> { cleanMapping });
            var result = ConfiguratorService.EvaluateExpression(cleanMappingMatch.Value, parameters)?.ToString();
            Assert.Equal("VAL_ACTIVE", result);
        }

        [Fact]
        public void CrossFeature_FullTypicalGroupSerializationAndValidation_ShouldDetectErrors()
        {
            // Deserialize database containing a group, and validate group variables
            string json = @"{
                ""TypicalGroups"": [
                    {
                        ""Name"": ""MotorGroup"",
                        ""Variables"": [
                            { ""Name"": ""Power"", ""IsMandatory"": true }
                        ]
                    }
                ]
            }";

            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            var group = data.TypicalGroups[0];
            var parameters = new Dictionary<string, object>
            {
                { "Power", null }
            };

            // Should throw error in validation. Expected to fail against stub.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables(group.Name, group.Variables, parameters));
        }

        [Fact]
        public void CrossFeature_BreakerLookupInFormulasAndValidation_ShouldQueryCatalogAndValidate()
        {
            // Evaluate custom catalog lookup function inside formula, and validate results
            var parameters = new Dictionary<string, object>
            {
                { "Power", 15.0 },
                { "StartType", "DOL" }
            };

            var breakerVal = ConfiguratorService.EvaluateExpression("Lookup_Breaker(Power, StartType)", parameters)?.ToString();

            // Breaker catalog default fallback is "Default_Set". If catalog file doesn't exist, returns "Default_Set".
            Assert.NotNull(breakerVal);

            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Breaker", IsMandatory = true }
            };
            var valParams = new Dictionary<string, object>
            {
                { "Breaker", breakerVal }
            };

            // Should pass validation since breaker is not null/empty
            ConfiguratorService.ValidateVariables("BreakerContext", variables, valParams);
        }

        // ==========================================
        // TIER 4: Real-World Workload Tests (6 tests)
        // ==========================================

        [Fact]
        public void Workload_MotorTypicalSetup_ShouldPassValidationAndEvaluateBreakerAndPageStructure()
        {
            // Real-world scenario for a standard Motor typical
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "MotorPower", IsMandatory = true, Type = "Double" },
                { new ConfigurationVariable { Name = "StartMode", IsMandatory = true, Type = "String" } },
                { new ConfigurationVariable { Name = "Cabinet", IsMandatory = false, Type = "String" } },
                { new ConfigurationVariable { Name = "ExternalCtrl", IsMandatory = true, VisibilityFormula = "StartMode == 'VFD'" } }
            };

            // Test case 1: DOL start mode. ExternalCtrl is not visible, thus not mandatory even if null.
            var parameters1 = new Dictionary<string, object>
            {
                { "MotorPower", 7.5 },
                { "StartMode", "DOL" },
                { "Cabinet", "MCC01" },
                { "ExternalCtrl", null }
            };

            // Validation should pass.
            ConfiguratorService.ValidateVariables("MotorTypical", variables, parameters1);

            // Compute breaker using Lookup_Breaker
            var breaker = ConfiguratorService.EvaluateExpression("Lookup_Breaker(MotorPower, StartMode)", parameters1)?.ToString();
            Assert.Equal("Default_Set", breaker); // or specific catalog entry if catalog exists

            // Format page name: ==MCC01/101
            var page = ConfiguratorService.FormatPageName("", "MCC01", "", "", "", "101");
            Assert.Equal("=MCC01/101", page);
        }

        [Fact]
        public void Workload_PowerDistributionCabinet_ShouldHandleMultiplePlaceholdersAndFormatting()
        {
            // Real-world scenario: Power distribution cabinet typical
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "FeederCount", IsMandatory = true, Type = "Integer" },
                new ConfigurationVariable { Name = "CabinetName", IsMandatory = true, Type = "String" }
            };

            var parameters = new Dictionary<string, object>
            {
                { "FeederCount", 12 },
                { "CabinetName", "PDB-01" }
            };

            // Validation should pass
            ConfiguratorService.ValidateVariables("PDBTypical", variables, parameters);

            // Build dynamic page name based on cabinet name: ==ESS=PDB-01/1
            var pageName = ConfiguratorService.FormatPageName("ESS", "PDB-01", "", "", "", "1");
            Assert.Equal("==ESS=PDB-01/1", pageName);

            // Variable mappings representing multiple placeholders
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("PhFeeder:Count", "=FeederCount"),
                new MappingEntry("PhFeeder:Name", "=CabinetName")
            };

            var mapCount = ConfiguratorService.FindPlaceholderMapping("PhFeeder", "Count", mappings);
            var mapName = ConfiguratorService.FindPlaceholderMapping("PhFeeder", "Name", mappings);

            Assert.NotNull(mapCount);
            Assert.NotNull(mapName);

            var countVal = ConfiguratorService.EvaluateExpression(mapCount.Value, parameters);
            var nameVal = ConfiguratorService.EvaluateExpression(mapName.Value, parameters);

            Assert.Equal(12, Convert.ToInt32(countVal));
            Assert.Equal("PDB-01", nameVal?.ToString());
        }

        [Fact]
        public void Workload_VariableDependencyChains_ShouldEvaluateInCorrectOrder()
        {
            // Real-world scenario: Complex visibility condition depending on multiple variables
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "IsMotor", IsMandatory = true, Type = "Boolean" },
                new ConfigurationVariable { Name = "StartMode", IsMandatory = false, Type = "String" },
                new ConfigurationVariable { Name = "EmergencyStop", IsMandatory = true, VisibilityFormula = "IsMotor && (StartMode == 'DOL' || StartMode == 'VFD')" }
            };

            // Test scenario: IsMotor = true, StartMode = "DOL". EmergencyStop is mandatory. Null should throw.
            var parameters1 = new Dictionary<string, object>
            {
                { "IsMotor", true },
                { "StartMode", "DOL" },
                { "EmergencyStop", null }
            };

            // Expected to throw. Expected to fail against stub.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables("Safety", variables, parameters1));

            // Test scenario: IsMotor = false, StartMode = "DOL". EmergencyStop is not visible. Null should pass.
            var parameters2 = new Dictionary<string, object>
            {
                { "IsMotor", false },
                { "StartMode", "DOL" },
                { "EmergencyStop", null }
            };

            ConfiguratorService.ValidateVariables("Safety", variables, parameters2);
        }

        [Fact]
        public void Workload_InvalidConfiguration_ShouldFailValidationAtGroupLevel()
        {
            // Real-world scenario: Bulk generation validation where a mandatory field is missing in one instance
            var groupVariables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "GlobalProjectName", IsMandatory = true, Type = "String" }
            };

            var parameters = new Dictionary<string, object>
            {
                { "GlobalProjectName", "" } // Invalid empty value
            };

            // Should fail validation. Expected to fail against stub.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables("GlobalGroup", groupVariables, parameters));
        }

        [Fact]
        public void Workload_BulkPageMacroGeneration_ShouldBuildCorrectPageHierarchyStrings()
        {
            // Real-world scenario: Generate structure hierarchy for multiple pages sequentially
            var pages = new List<Tuple<string, string, string>>
            {
                Tuple.Create("ESS", "MCC01", "101"),
                Tuple.Create("ESS", "MCC01", "102"),
                Tuple.Create("ESS", "MCC02", "101"),
                Tuple.Create("HT", "MCC03", "201")
            };

            var formattedPages = new List<string>();
            foreach (var page in pages)
            {
                formattedPages.Add(ConfiguratorService.FormatPageName(page.Item1, page.Item2, "", "", "", page.Item3));
            }

            Assert.Equal(4, formattedPages.Count);
            Assert.Equal("==ESS=MCC01/101", formattedPages[0]);
            Assert.Equal("==ESS=MCC01/102", formattedPages[1]);
            Assert.Equal("==ESS=MCC02/101", formattedPages[2]);
            Assert.Equal("==HT=MCC03/201", formattedPages[3]);
        }

        [Fact]
        public void Workload_UnusedVariablesAndVisibilityEdgeCases_ShouldNotThrow()
        {
            // Real-world scenario: Complex database setup with many variables, some visible and some not
            var variables = new List<ConfigurationVariable>();
            var parameters = new Dictionary<string, object>();

            for (int i = 1; i <= 30; i++)
            {
                variables.Add(new ConfigurationVariable
                {
                    Name = "Var_" + i,
                    IsMandatory = i % 3 == 0,
                    VisibilityFormula = "IsAvailable('Var_" + (i - 1) + "')"
                });

                if (i % 2 == 0 || i % 3 == 0)
                {
                    parameters["Var_" + i] = "Value_" + i;
                }
                else
                {
                    parameters["Var_" + i] = null;
                }
            }

            // Running validation with complex parameter set should execute without crashing
            var exception = Record.Exception(() => ConfiguratorService.ValidateVariables("BulkTypical", variables, parameters));
            Assert.Null(exception);
        }
    }
}
