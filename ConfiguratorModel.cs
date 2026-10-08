using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    /// <summary>
    /// Key-Value mapping entry class designed to be easily serialized/deserialized in JSON.
    /// </summary>
    public class MappingEntry : System.ComponentModel.INotifyPropertyChanged
    {
        private string _key;
        public string Key 
        { 
            get => _key; 
            set { if (_key != value) { _key = value; OnPropertyChanged(nameof(Key)); } } 
        }

        private string _value;
        public string Value 
        { 
            get => _value; 
            set { if (_value != value) { _value = value; OnPropertyChanged(nameof(Value)); } } 
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));

        public MappingEntry() { }

        public MappingEntry(string key, string value)
        {
            _key = key;
            _value = value;
        }
    }

    /// <summary>
    /// Represents a configuration variable in Cogineer Designer.
    /// </summary>
    public class ConfigurationVariable
    {
        public string Name { get; set; }
        public string Type { get; set; } // "String", "Boolean", "Integer", "Double"
        public string DisplayName { get; set; }
        public bool IsMandatory { get; set; }
        public string VisibilityFormula { get; set; }
        public List<string> PredefinedValues { get; set; }
        public string Category { get; set; }
        public string DefaultValue { get; set; }

        public ConfigurationVariable()
        {
            Type = "String";
            DisplayName = "";
            VisibilityFormula = "";
            PredefinedValues = new List<string>();
            Category = "";
            DefaultValue = "";
        }
    }

    /// <summary>
    /// Represents an instance of a Macro-Typical or Typical-Group inside a parent Typical-Group.
    /// </summary>
    public class TypicalInstance
    {
        public string TargetName { get; set; } // References MacroTypical or TypicalGroup Name
        public string Type { get; set; }       // "MacroTypical" or "TypicalGroup"
        public string IsActiveFormula { get; set; }
        public List<MappingEntry> VariableMappings { get; set; }

        public TypicalInstance()
        {
            TargetName = "";
            Type = "MacroTypical";
            IsActiveFormula = "";
            VariableMappings = new List<MappingEntry>();
        }
    }

    /// <summary>
    /// Represents a macro template element (page or window macro) assigned to a Macro-Typical.
    /// </summary>
    public class MacroElement
    {
        public string MacroPath { get; set; }
        public int VariantIndex { get; set; }
        public int RepresentationType { get; set; }
        public string IsActiveFormula { get; set; }
        public string PositionMode { get; set; } // "FromMacro", "Absolute", "Sequential"
        public double X { get; set; }
        public double Y { get; set; }
        public string XFormula { get; set; } = null;
        public string YFormula { get; set; } = null;
        public string Direction { get; set; } // "To the right", "To the left", "Up", "Down"
        public string Offset { get; set; } // Kept as string to support formulas later, currently numeric
        public string MacroType { get; set; } = "";  // "WindowMacro"/"PageMacro"/"SymbolMacro"
        public bool IsPrePlanning { get; set; } = false;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<MappingEntry> StructureMappings { get; set; }
        public List<MappingEntry> VariableMappings { get; set; }

        public MacroElement()
        {
            MacroPath = "";
            VariantIndex = 0;
            RepresentationType = 1;
            IsActiveFormula = "";
            PositionMode = "FromMacro";
            X = 0.0;
            Y = 0.0;
            Direction = "To the right";
            Offset = "0";
            StructureMappings = new List<MappingEntry>
            {
                new MappingEntry("==", ""), // Functional assignment
                new MappingEntry("=", ""),  // Function designation
                new MappingEntry("+", ""),  // Location designation
                new MappingEntry("&", ""),  // Document type
                new MappingEntry("UserDefined", ""),
                new MappingEntry("PageName", "101")
            };
            VariableMappings = new List<MappingEntry>();
        }
    }

    /// <summary>
    /// Represents a Macro-Typical containing variables and macro elements.
    /// </summary>
    public class MacroTypical
    {
        public string Name { get; set; }
        public List<ConfigurationVariable> Variables { get; set; }
        public List<MacroElement> Elements { get; set; }
        public List<VariantDefinition> Variants { get; set; }
        public List<PortDefinition> Ports { get; set; }

        public MacroTypical()
        {
            Name = "New_Macro_Typical";
            Variables = new List<ConfigurationVariable>();
            Elements = new List<MacroElement>();
            Variants = new List<VariantDefinition>();
            Ports = new List<PortDefinition>();
        }
    }

    /// <summary>
    /// Represents a Typical-Group containing configuration variables.
    /// </summary>
    public class TypicalGroup
    {
        public string Name { get; set; }
        public List<ConfigurationVariable> Variables { get; set; }
        public List<TypicalInstance> Instances { get; set; }
        public List<VariantDefinition> Variants { get; set; }

        public TypicalGroup()
        {
            Name = "New_Typical_Group";
            Variables = new List<ConfigurationVariable>();
            Instances = new List<TypicalInstance>();
            Variants = new List<VariantDefinition>();
        }
    }

    /// <summary>
    /// Represents a Configurator instance.
    /// </summary>
    public class Configurator
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> TypicalGroupRefs { get; set; }

        public Configurator()
        {
            Name = "New_Configurator";
            Description = "";
            TypicalGroupRefs = new List<string>();
        }
    }

    /// <summary>
    /// Represents settings for the eBUILD library.
    /// </summary>
    public class LibrarySettings
    {
        public string Unit { get; set; } // "Metric" or "Imperial"
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SelectedLanguages { get; set; }

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AvailableStructureIdentifiers { get; set; }

        public LibrarySettings()
        {
            Unit = "Metric";
            SelectedLanguages = new List<string> { "zh-CN", "en-US" };
            AvailableStructureIdentifiers = new List<string> { "==", "=", "+", "UserDefined", "PageName" };
        }
    }

    /// <summary>
    /// Root data structure representing the full Cogineer Designer database.
    /// </summary>
    public class ConfiguratorData
    {
        public List<MacroTypical> MacroTypicals { get; set; }
        public List<TypicalGroup> TypicalGroups { get; set; }
        public List<Configurator> Configurators { get; set; }
        public LibrarySettings Settings { get; set; }
        public List<ScriptTypical> Scripts { get; set; } = new List<ScriptTypical>();

        public ConfiguratorData()
        {
            MacroTypicals = new List<MacroTypical>();
            TypicalGroups = new List<TypicalGroup>();
            Configurators = new List<Configurator>();
            Settings = new LibrarySettings();
        }

        /// <summary>
        /// Repairs a freshly deserialized database: null-coalesces collections and removes
        /// duplicate structure-mapping keys so that <c>FirstOrDefault(Key == ...)</c> reads no
        /// longer silently lose a non-empty value to an earlier empty duplicate.
        /// </summary>
        public void Normalize()
        {
            MacroTypicals = MacroTypicals ?? new List<MacroTypical>();
            TypicalGroups = TypicalGroups ?? new List<TypicalGroup>();
            Configurators = Configurators ?? new List<Configurator>();
            Settings = Settings ?? new LibrarySettings();
            Scripts = Scripts ?? new List<ScriptTypical>();

            if (Settings != null)
            {
                Settings.SelectedLanguages = Settings.SelectedLanguages ?? new List<string>();
                Settings.AvailableStructureIdentifiers = Settings.AvailableStructureIdentifiers ?? new List<string>();
            }

            foreach (var mt in MacroTypicals)
            {
                if (mt == null) continue;
                mt.Variables = mt.Variables ?? new List<ConfigurationVariable>();
                mt.Elements = mt.Elements ?? new List<MacroElement>();
                mt.Variants = mt.Variants ?? new List<VariantDefinition>();
                mt.Ports = mt.Ports ?? new List<PortDefinition>();
                foreach (var el in mt.Elements)
                {
                    if (el == null) continue;
                    el.StructureMappings = DedupeStructureMappings(el.StructureMappings);
                    el.VariableMappings = el.VariableMappings ?? new List<MappingEntry>();
                }
            }

            foreach (var tg in TypicalGroups)
            {
                if (tg == null) continue;
                tg.Variables = tg.Variables ?? new List<ConfigurationVariable>();
                tg.Instances = tg.Instances ?? new List<TypicalInstance>();
                tg.Variants = tg.Variants ?? new List<VariantDefinition>();
                foreach (var inst in tg.Instances)
                {
                    if (inst != null) inst.VariableMappings = inst.VariableMappings ?? new List<MappingEntry>();
                }
            }

            foreach (var cfg in Configurators)
            {
                if (cfg != null) cfg.TypicalGroupRefs = cfg.TypicalGroupRefs ?? new List<string>();
            }
        }

        private static List<MappingEntry> DedupeStructureMappings(List<MappingEntry> mappings)
        {
            if (mappings == null) return new List<MappingEntry>();

            var byKey = new Dictionary<string, MappingEntry>(StringComparer.OrdinalIgnoreCase);
            var result = new List<MappingEntry>();
            foreach (var m in mappings)
            {
                if (m == null || string.IsNullOrEmpty(m.Key)) continue;

                if (byKey.TryGetValue(m.Key, out var existing))
                {
                    // Duplicate key: keep the last non-empty value so an earlier empty entry
                    // can never mask a later populated one.
                    if (!string.IsNullOrWhiteSpace(m.Value))
                    {
                        existing.Value = m.Value;
                    }
                }
                else
                {
                    var entry = new MappingEntry(m.Key, m.Value ?? "");
                    byKey[m.Key] = entry;
                    result.Add(entry);
                }
            }
            return result;
        }
    }

    public class ScriptTypical
    {
        public string Name { get; set; } = "";
        public string ScriptContent { get; set; } = "";
        public string TriggerPoint { get; set; } = "AfterGenerate";
        public string IsActiveFormula { get; set; } = "true";
        public int Order { get; set; } = 0;
    }

    /// <summary>
    /// Structured representation of an EPLAN page's structural designations, replacing ad-hoc
    /// string concatenation when building target page names.
    /// </summary>
    public class PageStructure
    {
        public string FunctionalAssignment { get; set; } = "";
        public string FunctionDesignation { get; set; } = "";
        public string LocationDesignation { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string UserDefined { get; set; } = "";
        public string PageName { get; set; } = "";

        /// <summary>
        /// Builds a <see cref="PageStructure"/> from macro element structure mappings, honouring
        /// the enabled structure identifiers in <paramref name="settings"/> and preferring the
        /// last non-empty value for a duplicated key.
        /// </summary>
        public static PageStructure FromStructureMappings(List<MappingEntry> structures, LibrarySettings settings)
        {
            var ps = new PageStructure();
            if (structures == null) return ps;

            bool Enabled(string id) =>
                settings == null || settings.AvailableStructureIdentifiers == null || settings.AvailableStructureIdentifiers.Contains(id);

            if (Enabled("==")) ps.FunctionalAssignment = GetValue(structures, "==");
            if (Enabled("=")) ps.FunctionDesignation = GetValue(structures, "=");
            if (Enabled("+")) ps.LocationDesignation = GetValue(structures, "+");
            if (Enabled("&")) ps.DocumentType = GetValue(structures, "&");
            if (Enabled("UserDefined") || Enabled("#")) ps.UserDefined = GetValue(structures, "UserDefined", "#");
            if (Enabled("PageName")) ps.PageName = GetValue(structures, "PageName");
            return ps;
        }

        public string ToFullPageName()
        {
            return ConfiguratorService.FormatPageName(FunctionalAssignment, FunctionDesignation, LocationDesignation, DocumentType, UserDefined, PageName);
        }

        private static string GetValue(List<MappingEntry> structures, params string[] keys)
        {
            for (int i = structures.Count - 1; i >= 0; i--)
            {
                var m = structures[i];
                if (m == null || string.IsNullOrEmpty(m.Key)) continue;
                foreach (var k in keys)
                {
                    if (string.Equals(m.Key, k, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Value))
                    {
                        return m.Value;
                    }
                }
            }
            return "";
        }
    }

    /// <summary>A named variant of a module with a selection condition.</summary>
    public class VariantDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Condition { get; set; } = "";
    }

    /// <summary>An interface/port on a module, used for future auto-connection.</summary>
    public class PortDefinition
    {
        public string Name { get; set; } = "";
        public string Direction { get; set; } = ""; // "Input" / "Output" / "InOut"
        public string SignalType { get; set; } = "";
        /// <summary>Placeholder variable this port maps to (interface → placeholder mapping).</summary>
        public string PlaceholderName { get; set; } = "";
    }

    /// <summary>Resolved component/article selection result.</summary>
    public class ArticleInfo
    {
        public string ArticleNumber { get; set; } = "";
        public string ValueSetName { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>Structured record of a generation run for audit/replay.</summary>
    public class GenerationRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string ConfiguratorName { get; set; } = "";
        public string Result { get; set; } = "";
        public string LogPath { get; set; } = "";
    }
}
