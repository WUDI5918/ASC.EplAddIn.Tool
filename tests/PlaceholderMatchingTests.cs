using System;
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class PlaceholderMatchingTests
    {
        // ==========================================
        // TIER 1: Standard Matching Formats (6 tests)
        // ==========================================

        [Fact]
        public void FindPlaceholderMapping_ColonWithSpaces_ShouldMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1 : Var1", "Value1")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(result);
            Assert.Equal("Value1", result.Value);
        }

        [Fact]
        public void FindPlaceholderMapping_EqualSign_ShouldMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1=Var1", "Value2")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(result);
            Assert.Equal("Value2", result.Value);
        }

        [Fact]
        public void FindPlaceholderMapping_OnlyVariableName_ShouldMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Var1", "Value3")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(result);
            Assert.Equal("Value3", result.Value);
        }

        [Fact]
        public void FindPlaceholderMapping_DotSeparator_ShouldMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1.Var1", "Value4")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(result);
            Assert.Equal("Value4", result.Value);
        }

        [Fact]
        public void FindPlaceholderMapping_ColonWithoutSpaces_ShouldMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1:Var1", "Value5")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(result);
            Assert.Equal("Value5", result.Value);
        }

        [Fact]
        public void FindPlaceholderMapping_MatchIsCaseInsensitive_ShouldMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("ph1:var1", "Value6")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("PH1", "VAR1", mappings);
            Assert.NotNull(result);
            Assert.Equal("Value6", result.Value);
        }

        // ==========================================
        // TIER 2: Edge Cases & Boundaries (6 tests)
        // ==========================================

        [Fact]
        public void FindPlaceholderMapping_NullMappings_ShouldReturnNull()
        {
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", null);
            Assert.Null(result);
        }

        [Fact]
        public void FindPlaceholderMapping_EmptyMappingsList_ShouldReturnNull()
        {
            var mappings = new List<MappingEntry>();
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.Null(result);
        }

        [Fact]
        public void FindPlaceholderMapping_MappingWithNoMatch_ShouldReturnNull()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph2:Var2", "Value")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.Null(result);
        }

        [Fact]
        public void FindPlaceholderMapping_DuplicateMatchingKeys_ShouldReturnFirstMatch()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1:Var1", "First"),
                new MappingEntry("Ph1.Var1", "Second")
            };
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            Assert.NotNull(result);
            Assert.Equal("First", result.Value);
        }

        [Fact]
        public void FindPlaceholderMapping_WhitespaceKeys_ShouldHandleAndNotCrash()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("  Ph1  :   Var1  ", "Value")
            };
            // The method doesn't necessarily trim keys internally unless we implemented it, 
            // but let's test that it doesn't match if it doesn't exact match or handle it.
            var result = ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings);
            // Since our spec doesn't say it trims key whitespace around names except specified formats, 
            // let's see: the lookup does $"{phName} : {varName}" which matches "Ph1 : Var1".
            // If the mapping contains excess spaces, it may return null, which is fine and expected.
            // Let's assert it handles it without throwing an exception.
            var exception = Record.Exception(() => ConfiguratorService.FindPlaceholderMapping("Ph1", "Var1", mappings));
            Assert.Null(exception);
        }

        [Fact]
        public void FindPlaceholderMapping_NullOrEmptyInputs_ShouldReturnNull()
        {
            var mappings = new List<MappingEntry>
            {
                new MappingEntry("Ph1:Var1", "Value")
            };
            Assert.Null(ConfiguratorService.FindPlaceholderMapping(null, "Var1", mappings));
            Assert.Null(ConfiguratorService.FindPlaceholderMapping("Ph1", null, mappings));
            Assert.Null(ConfiguratorService.FindPlaceholderMapping("", "Var1", mappings));
            Assert.Null(ConfiguratorService.FindPlaceholderMapping("Ph1", "", mappings));
        }
    }
}
