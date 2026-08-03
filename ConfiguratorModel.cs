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

        public MacroTypical()
        {
            Name = "New_Macro_Typical";
            Variables = new List<ConfigurationVariable>();
            Elements = new List<MacroElement>();
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

        public TypicalGroup()
        {
            Name = "New_Typical_Group";
            Variables = new List<ConfigurationVariable>();
            Instances = new List<TypicalInstance>();
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
        public bool UseSubSegmentLogic { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SelectedLanguages { get; set; }

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AvailableStructureIdentifiers { get; set; }

        public LibrarySettings()
        {
            Unit = "Metric";
            UseSubSegmentLogic = false;
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
    }

    public class ScriptTypical
    {
        public string Name { get; set; } = "";
        public string ScriptContent { get; set; } = "";
        public string TriggerPoint { get; set; } = "AfterGenerate";
        public string IsActiveFormula { get; set; } = "true";
        public int Order { get; set; } = 0;
    }
}
