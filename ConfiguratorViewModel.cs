using System.ComponentModel;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    /// <summary>
    /// View model for the configurator designer. Phase-1 MVVM extraction: centralizes the
    /// database instance and its dirty/save state so they are no longer raw code-behind fields.
    /// Selected-tab / selected-item / command plumbing remains in the window for now.
    /// </summary>
    public class ConfiguratorViewModel : INotifyPropertyChanged
    {
        private ConfiguratorData _database = new ConfiguratorData();
        private bool _isDirty;

        public ConfiguratorData Database
        {
            get => _database;
            set
            {
                if (!ReferenceEquals(_database, value))
                {
                    _database = value;
                    OnPropertyChanged(nameof(Database));
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged(nameof(IsDirty));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
