using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class LibrarySettingsDialog : Window
    {
        public LibrarySettings Settings { get; private set; }

        public LibrarySettingsDialog(LibrarySettings settings)
        {
            InitializeComponent();
            Settings = settings ?? new LibrarySettings();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load Unit
            if (string.Equals(Settings.Unit, "Imperial", StringComparison.OrdinalIgnoreCase))
            {
                CboUnit.SelectedIndex = 1;
            }
            else
            {
                CboUnit.SelectedIndex = 0;
            }

            // Load Sub-segment logic
            ChkSubSegment.IsChecked = Settings.UseSubSegmentLogic;

            // Load Languages
            var selectedLangs = Settings.SelectedLanguages ?? new List<string>();
            foreach (var child in PanelLanguages.Children)
            {
                if (child is CheckBox chk && chk.Tag is string lang)
                {
                    chk.IsChecked = selectedLangs.Contains(lang);
                }
            }

            // Load Structure Identifiers
            var selectedStructures = Settings.AvailableStructureIdentifiers ?? new List<string>();
            foreach (var child in PanelStructures.Children)
            {
                if (child is CheckBox chk && chk.Tag is string structId)
                {
                    chk.IsChecked = selectedStructures.Contains(structId);
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Save Unit
            var selectedItem = CboUnit.SelectedItem as ComboBoxItem;
            Settings.Unit = selectedItem?.Tag?.ToString() ?? "Metric";

            // Save Sub-segment logic
            Settings.UseSubSegmentLogic = ChkSubSegment.IsChecked == true;

            // Save Languages
            var selectedLangs = new List<string>();
            foreach (var child in PanelLanguages.Children)
            {
                if (child is CheckBox chk && chk.IsChecked == true && chk.Tag is string lang)
                {
                    selectedLangs.Add(lang);
                }
            }
            Settings.SelectedLanguages = selectedLangs;

            // Save Structure Identifiers
            var selectedStructures = new List<string>();
            foreach (var child in PanelStructures.Children)
            {
                if (child is CheckBox chk && chk.IsChecked == true && chk.Tag is string structId)
                {
                    selectedStructures.Add(structId);
                }
            }
            Settings.AvailableStructureIdentifiers = selectedStructures;

            DialogResult = true;
            Close();
        }
    }
}
