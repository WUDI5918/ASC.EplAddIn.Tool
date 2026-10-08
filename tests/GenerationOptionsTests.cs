using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    /// <summary>
    /// Guards the static generation-option switches (t9): safe defaults and the reset behaviour
    /// used by the generation entry points. Without this, a per-run option set by the silent
    /// action (JSON "Incremental"/"AbortOnFirstError") could leak into UI-triggered runs.
    /// </summary>
    public class GenerationOptionsTests
    {
        [Fact]
        public void GenerationOptions_Defaults_ShouldBeSafeDefaults()
        {
            // The safe defaults: full rebuild (not incremental) and abort + roll back on the
            // first error (never commit partial deletions).
            Assert.False(ConfiguratorService.IncrementalGeneration);
            Assert.True(ConfiguratorService.AbortOnFirstError);
        }

        [Fact]
        public void ResetGenerationOptions_ShouldRestoreSafeDefaults()
        {
            // Arrange: simulate a silent-action run that overrides both switches.
            ConfiguratorService.IncrementalGeneration = true;
            ConfiguratorService.AbortOnFirstError = false;

            // Act
            ConfiguratorService.ResetGenerationOptions();

            // Assert
            Assert.False(ConfiguratorService.IncrementalGeneration);
            Assert.True(ConfiguratorService.AbortOnFirstError);
        }

        [Fact]
        public void ResetGenerationOptions_ShouldBeIdempotent()
        {
            // Calling the reset twice (e.g. finally blocks of two consecutive runs) must not
            // change the result.
            ConfiguratorService.ResetGenerationOptions();
            ConfiguratorService.ResetGenerationOptions();

            Assert.False(ConfiguratorService.IncrementalGeneration);
            Assert.True(ConfiguratorService.AbortOnFirstError);
        }
    }
}
