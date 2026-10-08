using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorService
    {
        // ==================== VARIABLE DEPENDENCY ORDERING (P-8) ====================

        /// <summary>
        /// Returns the variables ordered so that a variable appears after every variable its
        /// <c>DefaultValue</c>/<c>VisibilityFormula</c> references. Falls back to the original
        /// order when a dependency cycle is detected.
        /// </summary>
        public static List<ConfigurationVariable> SortVariablesByDependency(List<ConfigurationVariable> variables)
        {
            if (variables == null || variables.Count <= 1) return variables;

            var names = new HashSet<string>(variables.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, ConfigurationVariable>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in variables) byName[v.Name] = v;

            // deps[name] = set of variable names that `name` references (must come before it).
            var deps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in variables)
            {
                var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectVariableReferences(v.DefaultValue, names, refs);
                CollectVariableReferences(v.VisibilityFormula, names, refs);
                refs.Remove(v.Name);
                deps[v.Name] = refs;
            }

            var result = new List<ConfigurationVariable>();
            var remaining = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            while (remaining.Count > 0)
            {
                var ready = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in remaining)
                {
                    bool allResolved = true;
                    foreach (var dep in deps[name])
                    {
                        if (remaining.Contains(dep)) { allResolved = false; break; }
                    }
                    if (allResolved) ready.Add(name);
                }

                if (ready.Count == 0)
                {
                    // Cycle: emit the rest in original order and stop.
                    foreach (var v in variables)
                    {
                        if (remaining.Contains(v.Name)) result.Add(v);
                    }
                    break;
                }

                foreach (var v in variables)
                {
                    if (remaining.Contains(v.Name) && ready.Contains(v.Name))
                    {
                        result.Add(v);
                        remaining.Remove(v.Name);
                    }
                }
            }

            return result;
        }

        private static void CollectVariableReferences(string formula, HashSet<string> knownNames, HashSet<string> refs)
        {
            if (string.IsNullOrWhiteSpace(formula)) return;
            foreach (Match m in Regex.Matches(formula, @"[A-Za-z_][A-Za-z0-9_]*"))
            {
                if (knownNames.Contains(m.Value)) refs.Add(m.Value);
            }
        }

        // ==================== VARIANT SELECTION (P-1) ====================

        /// <summary>
        /// Selects a named variant by evaluating each variant's condition. Returns the selected
        /// variant when exactly one matches; otherwise null. <paramref name="matchedNames"/> records
        /// every matching variant so callers can distinguish "zero matches" from "multiple matches".
        /// </summary>
        public static VariantDefinition ResolveVariant(List<VariantDefinition> variants, Dictionary<string, object> parameters, out List<string> matchedNames)
        {
            matchedNames = new List<string>();
            if (variants == null || variants.Count == 0) return null;

            foreach (var v in variants)
            {
                if (v == null) continue;
                if (EvaluateActiveFormula(v.Condition, parameters))
                {
                    matchedNames.Add(v.Name);
                }
            }

            if (matchedNames.Count == 1)
            {
                string matchedName = matchedNames[0];
                return variants.FirstOrDefault(v => v != null && string.Equals(v.Name, matchedName, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        /// <summary>
        /// Decides whether generation proceeds based on the declared variants (P-1 gating).
        /// </summary>
        /// <remarks>
        /// - No variants declared → <see cref="VariantGateResult.NoVariants"/> (generate as usual).
        /// - Exactly one variant condition matches → <see cref="VariantGateResult.Selected"/> with the
        ///   selected variant in <paramref name="selectedVariant"/>.
        /// - No variant condition matches → <see cref="VariantGateResult.NoMatch"/> (caller skips).
        /// - More than one variant condition matches → <see cref="VariantGateResult.MultipleMatches"/>
        ///   (caller aborts; consistent with pre-flight validation).
        /// </remarks>
        public static VariantGateResult ResolveVariantGate(List<VariantDefinition> variants, Dictionary<string, object> parameters, out VariantDefinition selectedVariant)
        {
            selectedVariant = null;
            if (variants == null || variants.Count == 0) return VariantGateResult.NoVariants;

            var selected = ResolveVariant(variants, parameters, out var matched);
            if (selected != null)
            {
                selectedVariant = selected;
                return VariantGateResult.Selected;
            }
            return matched.Count == 0 ? VariantGateResult.NoMatch : VariantGateResult.MultipleMatches;
        }
    }

    /// <summary>
    /// Outcome of P-1 variant gating: whether a typical/group declares variants and whether the
    /// declared variants select a unique one.
    /// </summary>
    public enum VariantGateResult
    {
        /// <summary>No variants declared — generate as usual.</summary>
        NoVariants,
        /// <summary>Exactly one variant condition matched — generate, remembering the selection.</summary>
        Selected,
        /// <summary>No variant condition matched — skip generation.</summary>
        NoMatch,
        /// <summary>More than one variant condition matched — abort generation.</summary>
        MultipleMatches
    }
}
