using System;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class PageNameFormattingTests
    {
        // ==========================================
        // TIER 1: Basic / Standard Test Cases (7 tests)
        // ==========================================

        [Fact]
        public void FormatPageName_WithAllIdentifiers_ShouldFormatCorrectly()
        {
            var result = ConfiguratorService.FormatPageName("ESS", "L1", "P1", "EBH", "USER", "101");
            Assert.Equal("==ESS=L1+P1&EBH#USER/101", result);
        }

        [Fact]
        public void FormatPageName_WithOnlyPageName_ShouldReturnPageNameWithSlash()
        {
            var result = ConfiguratorService.FormatPageName("", "", "", "", "", "101");
            Assert.Equal("/101", result);
        }

        [Fact]
        public void FormatPageName_WithFunctionalAssignmentAndPageName_ShouldFormatCorrectly()
        {
            var result = ConfiguratorService.FormatPageName("ESS", "", "", "", "", "101");
            Assert.Equal("==ESS/101", result);
        }

        [Fact]
        public void FormatPageName_WithFunctionDesignationAndPageName_ShouldFormatCorrectly()
        {
            var result = ConfiguratorService.FormatPageName("", "L1", "", "", "", "101");
            Assert.Equal("=L1/101", result);
        }

        [Fact]
        public void FormatPageName_WithLocationDesignationAndPageName_ShouldFormatCorrectly()
        {
            var result = ConfiguratorService.FormatPageName("", "", "P1", "", "", "101");
            Assert.Equal("+P1/101", result);
        }

        [Fact]
        public void FormatPageName_WithDocumentTypeAndPageName_ShouldFormatCorrectly()
        {
            var result = ConfiguratorService.FormatPageName("", "", "", "EBH", "", "101");
            Assert.Equal("&EBH/101", result);
        }

        [Fact]
        public void FormatPageName_WithUserDefinedAndPageName_ShouldFormatCorrectly()
        {
            var result = ConfiguratorService.FormatPageName("", "", "", "", "USER", "101");
            Assert.Equal("#USER/101", result);
        }

        // ==========================================
        // TIER 2: Edge Cases & Boundaries (6 tests)
        // ==========================================

        [Fact]
        public void FormatPageName_WithWhitespaceInputs_ShouldTrimCorrectly()
        {
            var result = ConfiguratorService.FormatPageName(" ESS   ", " L1 ", "  P1  ", " EBH ", " USER ", " 101 ");
            Assert.Equal("==ESS=L1+P1&EBH#USER/101", result);
        }

        [Fact]
        public void FormatPageName_WithNullInputs_ShouldSkipMissingIdentifiers()
        {
            var result = ConfiguratorService.FormatPageName(null, "L1", null, "EBH", null, "101");
            Assert.Equal("=L1&EBH/101", result);
        }

        [Fact]
        public void FormatPageName_WithEmptyInputs_ShouldOnlyReturnSlashAndPageName()
        {
            var result = ConfiguratorService.FormatPageName(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "202");
            Assert.Equal("/202", result);
        }

        [Fact]
        public void FormatPageName_WithSpecialCharactersInIdentifiers_ShouldPreserveSpecialCharacters()
        {
            var result = ConfiguratorService.FormatPageName("ES-S", "L_1", "P@1", "EB/H", "US#R", "101-A");
            Assert.Equal("==ES-S=L_1+P@1&EB/H#US#R/101-A", result);
        }

        [Fact]
        public void FormatPageName_WithLongPageName_ShouldFormatWithoutTruncation()
        {
            var longPage = new string('A', 255);
            var result = ConfiguratorService.FormatPageName("ESS", "L1", "P1", "EBH", "USER", longPage);
            Assert.Equal("==ESS=L1+P1&EBH#USER/" + longPage, result);
        }

        [Fact]
        public void FormatPageName_WithVaryingWhitespaceCases_ShouldTrimAllFields()
        {
            var result = ConfiguratorService.FormatPageName("\tESS\n", "\rL1", "P1\t", " EBH", "USER ", "101");
            Assert.Equal("==ESS=L1+P1&EBH#USER/101", result);
        }
    }
}
