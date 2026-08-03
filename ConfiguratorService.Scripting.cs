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
                .Where(s => s.TriggerPoint == triggerPoint)
                .Where(s =>
                {
                    try { return EvaluateActiveFormula(s.IsActiveFormula, parameters); }
                    catch { return true; }
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
                    engine.SetValue("Lookup", new Func<string, string, string, string, string>((table, retCol, searchCol, searchVal) => QueryGenericCatalog(table, retCol, searchCol, searchVal)));
                    engine.SetValue("Concat", new Func<object, object, string>((a, b) => (a?.ToString() ?? "") + (b?.ToString() ?? "")));

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
