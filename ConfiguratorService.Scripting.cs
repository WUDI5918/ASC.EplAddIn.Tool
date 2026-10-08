using System;
using System.Collections.Generic;
using System.Linq;
using Jint;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorService
    {
        public static void ExecuteScriptSteps(
            List<ScriptTypical> scripts,
            string triggerPoint,
            Dictionary<string, object> parameters,
            Action<string> logAction)
        {
            if (scripts == null || scripts.Count == 0) return;

            var activeScripts = scripts
                .Where(s => string.Equals(s.TriggerPoint, triggerPoint, StringComparison.OrdinalIgnoreCase))
                .Where(s =>
                {
                    try { return EvaluateActiveFormula(s.IsActiveFormula, parameters); }
                    catch (Exception ex)
                    {
                        // Activation-condition evaluation failed: default to running the script
                        // (unchanged fallback), but surface the error in the debug log.
                        LogDebug($"脚本 '{s.Name}' 激活条件评估异常，默认执行该脚本: {ex.Message}");
                        return true;
                    }
                })
                .OrderBy(s => s.Order)
                .ToList();

            foreach (var script in activeScripts)
            {
                try
                {
                    var engine = new Engine();
                    foreach (var kvp in parameters)
                    {
                        engine.SetValue(kvp.Key, kvp.Value);
                    }
                    engine.SetValue("Log", new Action<string>(msg => logAction?.Invoke($"[Script:{script.Name}] {msg}")));
                    engine.SetValue("Lookup", new Func<string, string, string, string, string>((table, retCol, searchCol, searchVal) => RuleEngine.QueryCatalog(table, retCol, searchCol, searchVal, _ruleEngineContext)));
                    // Concat supports variable arguments (2+), matching the main expression engine.
                    engine.Execute(@"
                        function Concat() {
                            var result = '';
                            for (var i = 0; i < arguments.length; i++) {
                                var a = arguments[i];
                                result += (a === null || a === undefined) ? '' : String(a);
                            }
                            return result;
                        }
                    ");

                    engine.Execute(script.ScriptContent);
                    logAction?.Invoke($"[Script:{script.Name}] executed successfully");
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"[Script:{script.Name}] ERROR: {ex.Message}");
                }
            }
        }
    }
}
