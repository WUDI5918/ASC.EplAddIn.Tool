using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class CycleDetectionTests
    {
        [Fact]
        public void HasTypicalGroupCycle_WithSelfReference_ShouldReturnTrue()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "A", Type = "TypicalGroup" });
            db.TypicalGroups.Add(groupA);

            Assert.True(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }

        [Fact]
        public void HasTypicalGroupCycle_WithIndirectCycle_ShouldReturnTrue()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            var groupB = new TypicalGroup { Name = "B" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "B", Type = "TypicalGroup" });
            groupB.Instances.Add(new TypicalInstance { TargetName = "A", Type = "TypicalGroup" });
            db.TypicalGroups.Add(groupA);
            db.TypicalGroups.Add(groupB);

            Assert.True(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }

        [Fact]
        public void HasTypicalGroupCycle_WithAcyclicGroups_ShouldReturnFalse()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            var groupB = new TypicalGroup { Name = "B" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "B", Type = "TypicalGroup" });
            db.TypicalGroups.Add(groupA);
            db.TypicalGroups.Add(groupB);

            Assert.False(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }

        [Fact]
        public void HasTypicalGroupCycle_WithMacroTypicalOnly_ShouldReturnFalse()
        {
            var db = new ConfiguratorData();
            var groupA = new TypicalGroup { Name = "A" };
            groupA.Instances.Add(new TypicalInstance { TargetName = "M1", Type = "MacroTypical" });
            db.TypicalGroups.Add(groupA);

            Assert.False(ConfiguratorService.HasTypicalGroupCycle(groupA, db));
        }
    }
}
