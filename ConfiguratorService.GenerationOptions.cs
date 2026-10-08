namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorService
    {
        /// <summary>
        /// When true, generation reuses existing target pages instead of deleting and recreating
        /// them. When false (default), existing pages are cleared before regeneration (full build).
        /// Per-run configuration: <see cref="ExecuteCogineerProjectBuilderAction"/> may set it from the
        /// JSON parameter file ("Incremental"); the generation entry points (<see cref="GenerateTypical"/>,
        /// <see cref="GenerateConfigurator"/>) restore the safe default in their finally block so
        /// UI-triggered runs never inherit a value left over from a silent action run.
        /// </summary>
        public static bool IncrementalGeneration { get; set; }

        /// <summary>
        /// When false, a single failing macro element is logged and skipped instead of aborting the
        /// whole transaction. Defaults to true (abort and roll back on the first error).
        /// NOTE: in skip mode (false) the run still commits a single transaction at the end, so pages
        /// deleted before a failing element are committed as deleted (see Generator.cs). Configured via
        /// the JSON parameter file ("AbortOnFirstError") and restored to the safe default by the
        /// generation entry points.
        /// </summary>
        public static bool AbortOnFirstError { get; set; } = true;

        /// <summary>
        /// Restores both generation option switches to their safe defaults (IncrementalGeneration=false,
        /// AbortOnFirstError=true). Called by the generation entry points in their finally block so that
        /// per-run options set by the silent action do not leak into UI-triggered generations.
        /// </summary>
        public static void ResetGenerationOptions()
        {
            IncrementalGeneration = false;
            AbortOnFirstError = true;
        }
    }
}
