using AutoSchematicConfigurator.EplAddIn.Tool;
using Newtonsoft.Json;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class JsonDeserializationTests
    {
        // ==========================================
        // TIER 1: Standard Deserialization (6 tests)
        // ==========================================

        [Fact]
        public void Deserialize_WithEmptyJson_ShouldReturnDefaultConfiguratorData()
        {
            string json = "{}";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.NotNull(data);
            Assert.NotNull(data.MacroTypicals);
            Assert.NotNull(data.TypicalGroups);
            Assert.NotNull(data.Configurators);
            Assert.NotNull(data.Settings);
        }

        [Fact]
        public void Deserialize_WithValidMacroTypical_ShouldParseCorrectly()
        {
            string json = @"{
                ""MacroTypicals"": [
                    {
                        ""Name"": ""TestMacro"",
                        ""Variables"": [
                            { ""Name"": ""Power"", ""Type"": ""Double"", ""DefaultValue"": ""15.0"" }
                        ]
                    }
                ]
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.Single(data.MacroTypicals);
            Assert.Equal("TestMacro", data.MacroTypicals[0].Name);
            Assert.Single(data.MacroTypicals[0].Variables);
            Assert.Equal("Power", data.MacroTypicals[0].Variables[0].Name);
            Assert.Equal("Double", data.MacroTypicals[0].Variables[0].Type);
        }

        [Fact]
        public void Deserialize_WithLibrarySettingsUnit_ShouldParseCorrectly()
        {
            string json = @"{
                ""Settings"": {
                    ""Unit"": ""Imperial""
                }
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.Equal("Imperial", data.Settings.Unit);
        }

        [Fact]
        public void Deserialize_WithConfigurator_ShouldParseNameAndTypicalGroups()
        {
            string json = @"{
                ""Configurators"": [
                    {
                        ""Name"": ""ProjectA"",
                        ""TypicalGroupRefs"": [""Group1"", ""Group2""]
                    }
                ]
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.Single(data.Configurators);
            Assert.Equal("ProjectA", data.Configurators[0].Name);
            Assert.Equal(2, data.Configurators[0].TypicalGroupRefs.Count);
        }

        [Fact]
        public void Deserialize_WithVariables_ShouldParseTypeAndPredefinedValues()
        {
            string json = @"{
                ""MacroTypicals"": [
                    {
                        ""Name"": ""M1"",
                        ""Variables"": [
                            {
                                ""Name"": ""Mode"",
                                ""Type"": ""String"",
                                ""PredefinedValues"": [""Auto"", ""Manual""]
                            }
                        ]
                    }
                ]
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            var variable = data.MacroTypicals[0].Variables[0];
            Assert.Equal("Mode", variable.Name);
            Assert.Equal(2, variable.PredefinedValues.Count);
            Assert.Contains("Auto", variable.PredefinedValues);
        }

        [Fact]
        public void Deserialize_WithTypicalGroup_ShouldParseSubSegmentsAndInstances()
        {
            string json = @"{
                ""TypicalGroups"": [
                    {
                        ""Name"": ""GroupA"",
                        ""Instances"": [
                            { ""Type"": ""MacroTypical"", ""TargetName"": ""M1"", ""IsActiveFormula"": ""true"" }
                        ]
                    }
                ]
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.Single(data.TypicalGroups);
            Assert.Equal("GroupA", data.TypicalGroups[0].Name);
            Assert.Single(data.TypicalGroups[0].Instances);
            Assert.Equal("M1", data.TypicalGroups[0].Instances[0].TargetName);
        }

        // ==========================================
        // TIER 2: Edge Cases & Duplication Prevention (6 tests)
        // ==========================================

        [Fact]
        public void Deserialize_SettingsSelectedLanguages_ShouldReplaceDefaultsNotAppend()
        {
            // The default list initialized in constructor is {"zh-CN", "en-US"}
            // Deserializing this JSON should replace the list to contain only "en-US"
            string json = @"{
                ""Settings"": {
                    ""SelectedLanguages"": [""en-US""]
                }
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);

            // This test is expected to fail against original code because it will append and result in 3 items.
            Assert.Single(data.Settings.SelectedLanguages);
            Assert.Equal("en-US", data.Settings.SelectedLanguages[0]);
        }

        [Fact]
        public void Deserialize_SettingsAvailableStructureIdentifiers_ShouldReplaceDefaultsNotAppend()
        {
            // Default list has 5 items. Deserializing this should replace them with these 2.
            string json = @"{
                ""Settings"": {
                    ""AvailableStructureIdentifiers"": [""=="", ""=""]
                }
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);

            // This test is expected to fail against original code because it will append and result in 7 items.
            Assert.Equal(2, data.Settings.AvailableStructureIdentifiers.Count);
            Assert.Contains("==", data.Settings.AvailableStructureIdentifiers);
            Assert.Contains("=", data.Settings.AvailableStructureIdentifiers);
        }

        [Fact]
        public void Deserialize_WithMalformedJson_ShouldThrowJsonException()
        {
            string json = "{ \"Settings\": { \"Unit\": \"Metric\"";
            Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<ConfiguratorData>(json));
        }

        [Fact]
        public void Deserialize_WithMissingProperties_ShouldNotThrowAndHaveDefaults()
        {
            string json = @"{
                ""Settings"": {
                    ""Unit"": null
                }
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.NotNull(data.Settings);
            Assert.Null(data.Settings.Unit);
        }

        [Fact]
        public void Deserialize_WithExtraFields_ShouldIgnoreUnknownProperties()
        {
            string json = @"{
                ""UnknownField"": 123,
                ""Settings"": {
                    ""Unit"": ""Metric"",
                    ""ExtraFieldInside"": ""hello""
                }
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.NotNull(data);
            Assert.Equal("Metric", data.Settings.Unit);
        }

        [Fact]
        public void Deserialize_WithNullValuesInCollections_ShouldHandleGracefully()
        {
            string json = @"{
                ""MacroTypicals"": null,
                ""Settings"": {
                    ""SelectedLanguages"": null
                }
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            Assert.Null(data.MacroTypicals);
            Assert.Null(data.Settings.SelectedLanguages);
        }

        [Fact]
        public void Deserialize_MacroElementStructureMappings_ShouldReplaceDefaultsNotAppend()
        {
            string json = @"{
                ""MacroTypicals"": [
                    {
                        ""Name"": ""MT1"",
                        ""Elements"": [
                            {
                                ""MacroPath"": ""test.ema"",
                                ""StructureMappings"": [
                                    { ""Key"": ""=="", ""Value"": ""MY_FUNCTIONAL_ASSIGNMENT"" },
                                    { ""Key"": ""PageName"", ""Value"": ""202"" }
                                ]
                            }
                        ]
                    }
                ]
            }";
            var data = JsonConvert.DeserializeObject<ConfiguratorData>(json);
            var element = data.MacroTypicals[0].Elements[0];

            // If ObjectCreationHandling.Replace is NOT set, this will fail because the default 5 items
            // are prepended and our FirstOrDefault lookup will match the default empty one!
            var mappings = element.StructureMappings;
            var faMapping = mappings.Find(m => m.Key == "==");
            Assert.NotNull(faMapping);
            Assert.Equal("MY_FUNCTIONAL_ASSIGNMENT", faMapping.Value);

            var pageMapping = mappings.Find(m => m.Key == "PageName");
            Assert.NotNull(pageMapping);
            Assert.Equal("202", pageMapping.Value);

            // Total mappings should be 2, not 7 (5 defaults + 2 custom)
            Assert.Equal(2, mappings.Count);
        }
    }
}
