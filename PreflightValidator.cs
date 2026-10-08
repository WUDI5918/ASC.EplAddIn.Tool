using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public enum PreflightSeverity { Info, Warning, Error }

    public class PreflightIssue
    {
        public PreflightSeverity Severity { get; set; }
        public string Message { get; set; }
    }

    public class PreflightReport
    {
        public List<PreflightIssue> Issues { get; } = new List<PreflightIssue>();

        public bool HasErrors => Issues.Any(i => i.Severity == PreflightSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == PreflightSeverity.Warning);

        public void Add(PreflightSeverity severity, string message) =>
            Issues.Add(new PreflightIssue { Severity = severity, Message = message });

        public override string ToString()
        {
            if (Issues.Count == 0) return "预检通过";
            return string.Join(Environment.NewLine, Issues.Select(i => $"[{i.Severity}] {i.Message}"));
        }
    }

    /// <summary>
    /// Pre-flight validator: runs before generation to detect missing references, cycles, invalid
    /// formulas, missing macro files, unpopulated mandatory variables and ambiguous variants.
    /// Produces a <see cref="PreflightReport"/> consumed by both the UI and the silent action.
    /// </summary>
    public static class PreflightValidator
    {
        /// <summary>
        /// Identifiers that never refer to user variables: system variables injected by the host,
        /// the engine's custom functions and NCalc-Edge built-ins (including the <c>and/or/not</c>
        /// keywords and the <c>true/false</c> literals, which the identifier scanner would otherwise
        /// mistake for variables). Used by the reference-integrity check.
        /// </summary>
        private static readonly HashSet<string> KnownNonVariableIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // System variables injected by IRuleEngineContext.
            "ProjectName", "User", "Date",
            // Custom functions implemented by RuleEngine.
            "Concat", "Lookup", "Lookup_Breaker", "IsAvailable", "IsNullEmpty",
            "Contains", "StartsWith", "EndsWith", "Upper", "Lower",
            // NCalc-Edge built-in functions / constants.
            "If", "Abs", "Floor", "Ceiling", "Round", "Max", "Min", "Sqrt", "Pow",
            "Sin", "Cos", "Tan", "Log", "Log10", "Exp", "Sign", "Truncate", "In", "NotIn", "PI", "E",
            // Operators / literals that match the identifier pattern.
            "and", "or", "not", "true", "false"
        };

        public static PreflightReport ValidateConfigurator(ConfiguratorData db, Configurator configurator, Dictionary<string, Dictionary<string, object>> groupParameters)
        {
            var report = new PreflightReport();
            if (db == null)
            {
                report.Add(PreflightSeverity.Error, "规则库为空。");
                return report;
            }
            db.Normalize();

            if (configurator == null)
            {
                report.Add(PreflightSeverity.Error, "配置器为空。");
                return report;
            }
            if (configurator.TypicalGroupRefs == null || configurator.TypicalGroupRefs.Count == 0)
            {
                report.Add(PreflightSeverity.Warning, $"配置器 '{configurator.Name}' 未关联任何规则组。");
                return report;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var groupRef in configurator.TypicalGroupRefs)
            {
                var group = db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, groupRef, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    report.Add(PreflightSeverity.Error, $"规则组 '{groupRef}' 在规则库中不存在。");
                    continue;
                }

                var parameters = (groupParameters != null && groupParameters.TryGetValue(group.Name, out var p)) ? p : new Dictionary<string, object>();
                ValidateGroup(group, db, parameters, report, visited);
            }

            return report;
        }

        private static void ValidateGroup(TypicalGroup group, ConfiguratorData db, Dictionary<string, object> parameters, PreflightReport report, HashSet<string> visited)
        {
            if (group == null) return;

            if (ConfiguratorService.HasTypicalGroupCycle(group, db))
            {
                report.Add(PreflightSeverity.Error, $"规则组 '{group.Name}' 存在循环引用。");
                return;
            }

            // Full variable scope of this group: visibility/activation formulas may reference any
            // variable of the group, so formula validity and reference integrity must be checked
            // against the whole scope, not just the formula's own variable.
            var scope = new HashSet<string>(
                group.Variables?.Select(v => v.Name) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (group.Variables != null)
            {
                foreach (var v in group.Variables)
                {
                    if (v == null) continue;
                    ValidateVariableFormula(group.Name, v, scope, report);
                }
                ValidateMandatory(group.Name, group.Variables, parameters, report);
            }

            if (group.Variants != null && group.Variants.Count > 0)
            {
                var sel = ConfiguratorService.ResolveVariant(group.Variants, parameters, out var matched);
                if (sel == null && matched.Count > 1)
                    report.Add(PreflightSeverity.Error, $"规则组 '{group.Name}' 多个变体命中 ({string.Join(", ", matched)})。");
                else if (sel == null && matched.Count == 0)
                    report.Add(PreflightSeverity.Warning, $"规则组 '{group.Name}' 无变体命中条件。");
            }

            if (group.Instances == null) return;
            foreach (var instance in group.Instances)
            {
                if (instance == null) continue;

                if (!string.IsNullOrWhiteSpace(instance.IsActiveFormula))
                {
                    var err = ConfiguratorService.ValidateFormula(instance.IsActiveFormula, scope);
                    if (err != null) report.Add(PreflightSeverity.Error, $"实例 '{instance.TargetName}' 的激活公式无效: {err}");
                    CheckFormulaReferences(group.Name, instance.IsActiveFormula, scope, report, $"实例 '{instance.TargetName}' 的激活公式");
                }

                if (instance.Type.Equals("MacroTypical", StringComparison.OrdinalIgnoreCase))
                {
                    var child = db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
                    if (child == null)
                    {
                        report.Add(PreflightSeverity.Error, $"实例 '{instance.TargetName}' 引用的宏规则不存在。");
                        continue;
                    }
                    ValidateMacroTypical(child, db, report);
                }
                else if (instance.Type.Equals("TypicalGroup", StringComparison.OrdinalIgnoreCase))
                {
                    var child = db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
                    if (child == null)
                    {
                        report.Add(PreflightSeverity.Error, $"实例 '{instance.TargetName}' 引用的规则组不存在。");
                        continue;
                    }
                    if (visited.Add(child.Name))
                    {
                        ValidateGroup(child, db, parameters, report, visited);
                    }
                }
                else
                {
                    report.Add(PreflightSeverity.Error, $"实例 '{instance.TargetName}' 的类型 '{instance.Type}' 无效。");
                }
            }
        }

        private static void ValidateMacroTypical(MacroTypical typical, ConfiguratorData db, PreflightReport report)
        {
            // Full variable scope of this typical: visibility/activation formulas may reference
            // any variable of the typical.
            var scope = new HashSet<string>(
                typical.Variables?.Select(v => v.Name) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (typical.Variables != null)
            {
                foreach (var v in typical.Variables)
                {
                    if (v != null) ValidateVariableFormula(typical.Name, v, scope, report);
                }
            }

            if (typical.Variants != null && typical.Variants.Count > 0)
            {
                foreach (var v in typical.Variants)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Condition)) continue;
                    var err = ConfiguratorService.ValidateFormula(v.Condition, scope);
                    if (err != null) report.Add(PreflightSeverity.Error, $"宏规则 '{typical.Name}' 的变体 '{v.Name}' 条件公式无效: {err}");
                    CheckFormulaReferences(typical.Name, v.Condition, scope, report, $"宏规则 '{typical.Name}' 的变体 '{v.Name}' 条件公式");
                }

                // P-1 gating is now enforced at generation time (0 matches → skip, multiple → abort),
                // so the pre-flight report must mirror the same severity mapping as the group branch.
                var sel = ConfiguratorService.ResolveVariant(typical.Variants, null, out var matched);
                if (sel == null && matched.Count > 1)
                    report.Add(PreflightSeverity.Error, $"宏规则 '{typical.Name}' 多个变体命中条件，生成时将中止。");
                else if (sel == null && matched.Count == 0)
                    report.Add(PreflightSeverity.Warning, $"宏规则 '{typical.Name}' 无变体命中条件，生成时将跳过该规则。");
            }

            if (typical.Elements == null) return;
            foreach (var el in typical.Elements)
            {
                if (el == null) continue;

                if (!string.IsNullOrWhiteSpace(el.IsActiveFormula))
                {
                    var err = ConfiguratorService.ValidateFormula(el.IsActiveFormula, scope);
                    if (err != null) report.Add(PreflightSeverity.Error, $"宏规则 '{typical.Name}' 的元素激活公式无效: {err}");
                    CheckFormulaReferences(typical.Name, el.IsActiveFormula, scope, report, $"宏规则 '{typical.Name}' 的元素激活公式");
                }

                if (!string.IsNullOrWhiteSpace(el.MacroPath))
                {
                    string resolved = ConfiguratorService.ResolveMacroPath(el.MacroPath);
                    if (!File.Exists(resolved))
                    {
                        report.Add(PreflightSeverity.Error, $"宏文件不存在: '{el.MacroPath}'");
                    }
                }
            }
        }

        private static void ValidateVariableFormula(string context, ConfigurationVariable variable, IEnumerable<string> scopeVariableNames, PreflightReport report)
        {
            if (string.IsNullOrWhiteSpace(variable.VisibilityFormula)) return;

            // Validate against the full scope (not just the variable's own name) so a formula
            // referencing a sibling variable is not falsely reported as invalid.
            var err = ConfiguratorService.ValidateFormula(variable.VisibilityFormula, scopeVariableNames);
            if (err != null) report.Add(PreflightSeverity.Warning, $"变量 '{context}.{variable.Name}' 的可见性公式无效: {err}");

            CheckFormulaReferences(context, variable.VisibilityFormula, scopeVariableNames, report, $"变量 '{context}.{variable.Name}' 的可见性公式");
        }

        /// <summary>
        /// Reference-integrity check: scans a formula for identifier tokens and reports any token
        /// that is neither a variable of the current scope, nor a system variable, nor a known
        /// built-in function/operator. Such a token will throw "Parameter was not defined" (or be
        /// silently treated as null) at evaluation time, so flagging it early avoids subtle
        /// generation-time failures. String literals and <c>[bracketed]</c> identifiers are skipped
        /// so their content is not misread as a reference.
        /// </summary>
        private static void CheckFormulaReferences(string context, string formula, IEnumerable<string> scopeVariableNames, PreflightReport report, string what)
        {
            if (string.IsNullOrWhiteSpace(formula)) return;

            string clean = formula.Trim();
            if (clean.StartsWith("=")) clean = clean.Substring(1).Trim();

            var known = scopeVariableNames as HashSet<string> ?? new HashSet<string>(scopeVariableNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string stripped = Regex.Replace(clean, @"'(?:[^']|'')*'|""(?:[^""\\]|\\.)*""|\[[^\]]*\]", " ");

            foreach (Match m in Regex.Matches(stripped, @"[A-Za-z_][A-Za-z0-9_]*"))
            {
                if (known.Contains(m.Value) || KnownNonVariableIdentifiers.Contains(m.Value)) continue;
                report.Add(PreflightSeverity.Warning, $"'{what}' 引用了未定义变量 '{m.Value}'（{context}）。");
            }
        }

        private static void ValidateMandatory(string context, List<ConfigurationVariable> variables, Dictionary<string, object> parameters, PreflightReport report)
        {
            var safe = parameters ?? new Dictionary<string, object>();
            foreach (var v in variables)
            {
                if (v == null || !v.IsMandatory) continue;

                bool visible = true;
                if (!string.IsNullOrWhiteSpace(v.VisibilityFormula))
                {
                    visible = ConfiguratorService.EvaluateActiveFormula(v.VisibilityFormula, safe);
                }

                if (!visible) continue;

                var val = safe.ContainsKey(v.Name) ? safe[v.Name] : null;
                if (val == null || string.IsNullOrWhiteSpace(val.ToString()))
                {
                    report.Add(PreflightSeverity.Error, $"必填参数 '{context}.{v.Name}' 未填写。");
                }
            }
        }
    }
}
