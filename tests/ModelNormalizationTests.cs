using System.Collections.Generic;
using System.Linq;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class ModelNormalizationTests
    {
        [Fact]
        public void Normalize_ShouldDedupeStructureMappings_KeepNonEmptyValue()
        {
            var db = new ConfiguratorData();
            var mt = new MacroTypical { Name = "MT1" };
            var el = new MacroElement { MacroPath = "x.ema" };
            el.StructureMappings = new List<MappingEntry>
            {
                new MappingEntry("==", ""),
                new MappingEntry("=", ""),
                new MappingEntry("PageName", ""),     // empty first
                new MappingEntry("PageName", "101")   // duplicate with a value
            };
            mt.Elements.Add(el);
            db.MacroTypicals.Add(mt);

            db.Normalize();

            var pageMappings = el.StructureMappings.Where(m => m.Key == "PageName").ToList();
            Assert.Single(pageMappings);
            Assert.Equal("101", pageMappings[0].Value);
        }

        [Fact]
        public void PageStructure_FromStructureMappings_ShouldPreferNonEmptyDuplicate()
        {
            var structures = new List<MappingEntry>
            {
                new MappingEntry("==", ""),
                new MappingEntry("==", "ESS"),
                new MappingEntry("PageName", "101")
            };

            var ps = PageStructure.FromStructureMappings(structures, null);

            Assert.Equal("ESS", ps.FunctionalAssignment);
            Assert.Equal("101", ps.PageName);
            Assert.Equal("==ESS/101", ps.ToFullPageName());
        }

        [Fact]
        public void PageStructure_FromStructureMappings_ShouldHonorDisabledIdentifiers()
        {
            var structures = new List<MappingEntry>
            {
                new MappingEntry("==", "ESS"),
                new MappingEntry("PageName", "101")
            };
            var settings = new LibrarySettings
            {
                AvailableStructureIdentifiers = new List<string> { "PageName" }
            };

            var ps = PageStructure.FromStructureMappings(structures, settings);

            Assert.Equal("", ps.FunctionalAssignment);
            Assert.Equal("101", ps.PageName);
            Assert.Equal("/101", ps.ToFullPageName());
        }

        [Fact]
        public void PageStructure_FromStructureMappings_WithDocumentType_ShouldProduceFullPageName()
        {
            var structures = new List<MappingEntry>
            {
                new MappingEntry("==", "ESS"),
                new MappingEntry("=", "L1"),
                new MappingEntry("&", "PAGE"),
                new MappingEntry("PageName", "101")
            };

            var ps = PageStructure.FromStructureMappings(structures, null);

            Assert.Equal("ESS", ps.FunctionalAssignment);
            Assert.Equal("L1", ps.FunctionDesignation);
            Assert.Equal("PAGE", ps.DocumentType);
            Assert.Equal("==ESS=L1&PAGE/101", ps.ToFullPageName());
        }

        [Fact]
        public void PageStructure_FromStructureMappings_WithDocumentTypeDisabled_ShouldSkipDocumentType()
        {
            var structures = new List<MappingEntry>
            {
                new MappingEntry("==", "ESS"),
                new MappingEntry("&", "PAGE"),
                new MappingEntry("PageName", "101")
            };
            var settings = new LibrarySettings
            {
                AvailableStructureIdentifiers = new List<string> { "==", "PageName" }
            };

            var ps = PageStructure.FromStructureMappings(structures, settings);

            Assert.Equal("", ps.DocumentType);
            Assert.Equal("==ESS/101", ps.ToFullPageName());
        }

        [Fact]
        public void Normalize_ShouldDedupeDocumentType_KeepNonEmptyValue()
        {
            var db = new ConfiguratorData();
            var mt = new MacroTypical { Name = "MT1" };
            var el = new MacroElement { MacroPath = "x.ema" };
            el.StructureMappings = new List<MappingEntry>
            {
                new MappingEntry("&", ""),
                new MappingEntry("&", "PAGE")
            };
            mt.Elements.Add(el);
            db.MacroTypicals.Add(mt);

            db.Normalize();

            var docTypeMappings = el.StructureMappings.Where(m => m.Key == "&").ToList();
            Assert.Single(docTypeMappings);
            Assert.Equal("PAGE", docTypeMappings[0].Value);
        }

        [Fact]
        public void MacroElement_DefaultStructureMappings_ShouldIncludeDocumentTypeKey()
        {
            var el = new MacroElement();

            Assert.NotNull(el.StructureMappings);
            Assert.Contains(el.StructureMappings, m => m.Key == "&");
        }
    }
}
