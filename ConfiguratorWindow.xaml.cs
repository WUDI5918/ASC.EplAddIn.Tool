using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Xml.Linq;
using OfficeOpenXml;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorWindow : Window
    {
        private const string DbFileName = "configurator_data.json";
        private string _dbFilePath;
        private ConfiguratorData _db = new ConfiguratorData();
        private int _selectedTab = 0; // 0: Macro-Typical, 1: Typical-Group, 2: Configurator
        internal object _selectedItem = null;
        private MacroElement _targetElementForMacroSelection = null;
        private bool _isAddingNewElement = false;
        private List<EplanMacroInfo> _allProjectMacros = new List<EplanMacroInfo>();
        private MacroElement _selectedMacroElement = null;
        private ConfigurationVariable _selectedConfigVariable = null;
        private System.Windows.Threading.DispatcherTimer _toastTimer;
       private List<CheckableAddElement> _allModalAddElements = new List<CheckableAddElement>();

        private bool _isDirty = false;
        private System.Windows.Threading.DispatcherTimer _saveDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _searchDebounceTimer;

       public ConfiguratorWindow()
       {
           InitializeComponent();
           ModernWpf.ThemeManager.SetRequestedTheme(this, ModernWpf.ElementTheme.Dark);
           InitializeDbPath();
           // Defer I/O-heavy init to Loaded so the window appears immediately
           this.Loaded += async (s, e) =>
           {
               await Task.Run(() => LoadDatabase());
               RefreshSidebar();
               UpdateWorkspaceSelection();
           };
       }

        private void MarkDirty()
        {
            _isDirty = true;
            if (_saveDebounceTimer == null)
            {
                _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                _saveDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
                _saveDebounceTimer.Tick += (_, __) =>
                {
                    _saveDebounceTimer.Stop();
                    if (_isDirty) { _isDirty = false; FlushSave(); }
                };
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void ForceFlushSave()
        {
            if (_saveDebounceTimer != null) _saveDebounceTimer.Stop();
            if (_isDirty) { _isDirty = false; FlushSave(); }
        }

       private void InitializeDbPath()
        {
            // Save settings in the project directory for portability
            _dbFilePath = Path.Combine(@"c:\Users\62789\source\repos\ASC.EplAddIn.Tool", DbFileName);
        }

        // ==================== DATABASE LOAD / SAVE ====================
        private void LoadDatabase()
        {
            try
            {
                if (File.Exists(_dbFilePath))
                {
                    string json = File.ReadAllText(_dbFilePath);
                    _db = JsonConvert.DeserializeObject<ConfiguratorData>(json);
                }
                
                if (_db == null)
                {
                    _db = new ConfiguratorData();
                }

                // Add sample data if completely empty
                if (_db.MacroTypicals.Count == 0 && _db.TypicalGroups.Count == 0 && _db.Configurators.Count == 0)
                {
                    CreateSampleData();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"åŠ è½½é…ç½®å¤±è´¥: {ex.Message}", "é…ç½®å™¨æç¤º", MessageBoxButton.OK, MessageBoxImage.Warning);
                _db = new ConfiguratorData();
            }
        }

        private void FlushSave()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_db, Formatting.Indented);
                File.WriteAllText(_dbFilePath, json);
                TxtStatus.Text = $"å·²æˆåŠŸä¿å­˜é…ç½®åˆ°æœ¬åœ°ï¼({DateTime.Now:HH:mm:ss})";
                TxtStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"ä¿å­˜é…ç½®å‡ºé”™: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
        }

        private void CreateSampleData()
        {
            // 1. Create a MacroTypical
            var typical = new MacroTypical
            {
                Name = "Main_Distribution_Cabinet_Macro"
            };
            typical.Variables.Add(new ConfigurationVariable
            {
                Name = "MCCB_Current_Value",
                DisplayName = "è®¾å®šå¡‘å£³æ–­è·¯å™¨ç”µæµå€¼",
                Type = "String",
                IsMandatory = true,
                PredefinedValues = new List<string> { "160A", "250A", "320A", "630A" }
            });
            typical.Variables.Add(new ConfigurationVariable
            {
                Name = "MCCB_Pole_Number",
                DisplayName = "è®¾å®šå¡‘å£³æ–­è·¯å™¨æžæ•°",
                Type = "String",
                IsMandatory = true,
                PredefinedValues = new List<string> { "3P", "4P" }
            });
            
            var element = new MacroElement
            {
                MacroPath = @"$(MD_MACROS)\Main_Incoming_Basic_Framework_3P.ema",
                VariantIndex = 0,
                IsActiveFormula = "=MCCB_Pole_Number=='3P'"
            };
            element.VariableMappings.Add(new MappingEntry("Connection_Cross-section_diameter", "=(MCCB_Current_Value=='320A' ? '185' : '240')"));
            typical.Elements.Add(element);

            _db.MacroTypicals.Add(typical);

            // 2. Create a TypicalGroup and nest the MacroTypical inside it
            var group = new TypicalGroup 
            { 
                Name = "Main Distribution Cabinet" 
            };
            group.Variables.Add(new ConfigurationVariable 
            { 
                Name = "Function_designation", 
                DisplayName = "å›¾çº¸é«˜å±‚ä»£å· (==)",
                Type = "String"
            });
            group.Variables.Add(new ConfigurationVariable 
            { 
                Name = "Use_Cabinet", 
                DisplayName = "æ˜¯å¦ç”Ÿæˆé…ç”µæŸœå›¾çº¸",
                Type = "Boolean"
            });
            group.Variables.Add(new ConfigurationVariable 
            { 
                Name = "Cabinet_Current", 
                DisplayName = "é…ç”µæŸœé¢å®šç”µæµ",
                Type = "String",
                VisibilityFormula = "=Use_Cabinet==true",
                PredefinedValues = new List<string> { "160A", "250A", "320A", "630A" }
            });

            // Add the MacroTypical instance inside the TypicalGroup
            var instance = new TypicalInstance
            {
                TargetName = "Main_Distribution_Cabinet_Macro",
                Type = "MacroTypical",
                IsActiveFormula = "=Use_Cabinet==true"
            };
            instance.VariableMappings.Add(new MappingEntry("MCCB_Current_Value", "=Cabinet_Current"));
            instance.VariableMappings.Add(new MappingEntry("MCCB_Pole_Number", "3P")); // Constant mapping
            group.Instances.Add(instance);

            _db.TypicalGroups.Add(group);

            // 3. Create a Configurator and reference the TypicalGroup
            var configurator = new Configurator 
            { 
                Name = "Configurator_P8", 
                Description = "ä¸»é…ç”µæŸœåŽŸç†å›¾è‡ªåŠ¨é…ç½®é¡¹ç›®" 
            };
            configurator.TypicalGroupRefs.Add("Main Distribution Cabinet");
            _db.Configurators.Add(configurator);
        }

        // ==================== SIDEBAR UI LOGIC ====================
        private void RefreshSidebar()
        {
            if (LstSidebarItems == null || TxtSearch == null) return;

            string searchTxt = TxtSearch.Text.Trim().ToLower();
            LstSidebarItems.SelectionChanged -= LstSidebarItems_SelectionChanged;

            int count = 0;
            string emptyText = "No Items";

            if (_selectedTab == 0)
            {
                var list = _db.MacroTypicals.Where(i => string.IsNullOrEmpty(searchTxt) || i.Name.ToLower().Contains(searchTxt)).ToList();
                LstSidebarItems.ItemsSource = list;
                count = list.Count;
                emptyText = "No Macro Typicals";
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "å®è§„åˆ™";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.ToolTip = "æ–°å¢žå®è§„åˆ™";
                    BtnAddNewItem.Visibility = Visibility.Visible;
                }
            }
            else if (_selectedTab == 1)
            {
                var list = _db.TypicalGroups.Where(i => string.IsNullOrEmpty(searchTxt) || i.Name.ToLower().Contains(searchTxt)).ToList();
                LstSidebarItems.ItemsSource = list;
                count = list.Count;
                emptyText = "No Typical Groups";
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "è§„åˆ™ç»„";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.ToolTip = "æ–°å¢žè§„åˆ™ç»„";
                    BtnAddNewItem.Visibility = Visibility.Visible;
                }
            }
            else if (_selectedTab == 2)
            {
                var list = _db.Configurators.Where(i => string.IsNullOrEmpty(searchTxt) || i.Name.ToLower().Contains(searchTxt)).ToList();
                LstSidebarItems.ItemsSource = list;
                count = list.Count;
                emptyText = "No configurators";
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "é…ç½®å™¨";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.ToolTip = "æ–°å¢žé…ç½®å™¨";
                    BtnAddNewItem.Visibility = Visibility.Visible;
                }
            }
            else // _selectedTab == 3 (Project Builder)
            {
                var list = _db.Configurators.Where(i => string.IsNullOrEmpty(searchTxt) || i.Name.ToLower().Contains(searchTxt)).ToList();
                LstSidebarItems.ItemsSource = list;
                count = list.Count;
                emptyText = "No configurators";
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "Project Builder";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.Visibility = Visibility.Collapsed;
                }
            }

            if (GridLeftEmptyState != null && TxtLeftEmptyText != null)
            {
                if (count == 0)
                {
                    GridLeftEmptyState.Visibility = Visibility.Visible;
                    TxtLeftEmptyText.Text = emptyText;
                }
                else
                {
                    GridLeftEmptyState.Visibility = Visibility.Collapsed;
                }
            }

            LstSidebarItems.SelectionChanged += LstSidebarItems_SelectionChanged;
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (RadTabMacro == null || RadTabGroup == null || RadTabConfig == null || RadTabBuilder == null) return;

            if (RadTabMacro.IsChecked == true) _selectedTab = 0;
            else if (RadTabGroup.IsChecked == true) _selectedTab = 1;
            else if (RadTabConfig.IsChecked == true) _selectedTab = 2;
            else if (RadTabBuilder.IsChecked == true) _selectedTab = 3;
            else _selectedTab = 0;

            // Auto-expand left selection panel if it is collapsed
            if (ColSelectionArea != null && (ColSelectionArea.Width.Value == 0 || ColSelectionArea.ActualWidth <= 10))
            {
                ColSelectionArea.Width = new GridLength(240);
            }

            _selectedItem = null;
            RefreshSidebar();
            UpdateWorkspaceSelection();
            UpdateBreadcrumbs();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
                _searchDebounceTimer.Tick += (_, __) =>
                {
                    _searchDebounceTimer.Stop();
                    RefreshSidebar();
                };
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void LstSidebarItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedItem = LstSidebarItems.SelectedItem;
            UpdateWorkspaceSelection();
        }

        private void BtnAddNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (ModalCreateItem == null || TxtModalCreateTitle == null || TxtModalCreateName == null) return;

            string title = "Create Item";
            string defaultPrefix = "";

            if (_selectedTab == 0)
            {
                title = "Create Macro Typical";
                defaultPrefix = "MT_";
            }
            else if (_selectedTab == 1)
            {
                title = "Create Typical Group";
                defaultPrefix = "TG_";
            }
            else if (_selectedTab == 2)
            {
                title = "Create configurator";
                defaultPrefix = ""; // default to empty
            }
            else
            {
                return; // Tab 3 is Project Builder, we don't add new items
            }

            TxtModalCreateTitle.Text = title;
            TxtModalCreateName.Text = defaultPrefix;
            ModalCreateItem.Visibility = Visibility.Visible;
            TxtModalCreateName.Focus();
            TxtModalCreateName.SelectionStart = defaultPrefix.Length;

            // Trigger TextChanged to validate confirm button state
            TxtModalCreateName_TextChanged(null, null);
        }

        // ==================== WORKSPACE UI LOGIC ====================
        private void UpdateWorkspaceSelection()
        {
            _selectedMacroElement = null;
            _selectedConfigVariable = null;
            if (ItrElements != null) ItrElements.SelectedItem = null;
            if (ItrVariables != null) ItrVariables.SelectedItem = null;
            if (GridDetailsEmpty != null) GridDetailsEmpty.Visibility = Visibility.Visible;
            if (GridMacroDetails != null) GridMacroDetails.Visibility = Visibility.Collapsed;
            if (GridVariableDetails != null) GridVariableDetails.Visibility = Visibility.Collapsed;

            if (_selectedItem == null)
            {
                GridLanding.Visibility = Visibility.Visible;
                UpdateBreadcrumbs();
                return;
            }

            GridLanding.Visibility = Visibility.Collapsed;
            TxtSelectedName.LostFocus -= TxtSelectedName_LostFocus;

            // Reset visibility of all major workspace panels
            ExpVariables.Visibility = Visibility.Collapsed;
            ExpElements.Visibility = Visibility.Collapsed;
            ExpInstances.Visibility = Visibility.Collapsed;
            GridConfiguratorDetails.Visibility = Visibility.Collapsed;
            GridProjectBuilderForm.Visibility = Visibility.Collapsed;

            // Rename control state
            TxtSelectedName.IsReadOnly = false;

            if (_selectedTab == 0 && _selectedItem is MacroTypical typical)
            {
                TxtSelectedName.Text = typical.Name;
                ExpVariables.Visibility = Visibility.Visible;
                ExpElements.Visibility = Visibility.Visible;
                
                BindVariablesList(typical.Variables);
                BindElementsList(typical.Elements);
            }
            else if (_selectedTab == 1 && _selectedItem is TypicalGroup group)
            {
                TxtSelectedName.Text = group.Name;
                ExpVariables.Visibility = Visibility.Visible;
                ExpInstances.Visibility = Visibility.Visible;
                
                BindVariablesList(group.Variables);
                BindInstancesList(group.Instances);
            }
            else if (_selectedTab == 2 && _selectedItem is Configurator config)
            {
                TxtSelectedName.Text = config.Name;
                GridConfiguratorDetails.Visibility = Visibility.Visible;
                
                TxtConfigDescription.Text = config.Description;
                BindConfigGroupsList(config);
            }
            else if (_selectedTab == 3 && _selectedItem is Configurator runConfig)
            {
                TxtSelectedName.Text = runConfig.Name;
                TxtSelectedName.IsReadOnly = true;
                GridProjectBuilderForm.Visibility = Visibility.Visible;
                
                RenderProjectBuilderForm(runConfig);
            }

            TxtSelectedName.LostFocus += TxtSelectedName_LostFocus;
            UpdateBreadcrumbs();
        }

        private void TxtSelectedName_LostFocus(object sender, RoutedEventArgs e)
        {
            string newName = TxtSelectedName.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            if (_selectedItem is MacroTypical typical) typical.Name = newName;
            else if (_selectedItem is TypicalGroup group) group.Name = newName;
            else if (_selectedItem is Configurator config) config.Name = newName;

            RefreshSidebar();
        }

        private void BtnCopySidebarItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            object item = btn.DataContext;
            if (item == null) return;

            object copiedItem = null;

            if (item is MacroTypical typical)
            {
                string json = JsonConvert.SerializeObject(typical);
                var copy = JsonConvert.DeserializeObject<MacroTypical>(json);
                copy.Name = GetUniqueCopyName(typical.Name, _db.MacroTypicals.Select(x => x.Name));
                _db.MacroTypicals.Add(copy);
                copiedItem = copy;
            }
            else if (item is TypicalGroup group)
            {
                string json = JsonConvert.SerializeObject(group);
                var copy = JsonConvert.DeserializeObject<TypicalGroup>(json);
                copy.Name = GetUniqueCopyName(group.Name, _db.TypicalGroups.Select(x => x.Name));
                _db.TypicalGroups.Add(copy);
                copiedItem = copy;
            }
            else if (item is Configurator config)
            {
                string json = JsonConvert.SerializeObject(config);
                var copy = JsonConvert.DeserializeObject<Configurator>(json);
                copy.Name = GetUniqueCopyName(config.Name, _db.Configurators.Select(x => x.Name));
                _db.Configurators.Add(copy);
                copiedItem = copy;
            }

            if (copiedItem != null)
            {
                _selectedItem = copiedItem;
                RefreshSidebar();
                LstSidebarItems.SelectedItem = _selectedItem;
                UpdateWorkspaceSelection();
                MarkDirty();
            }
        }

        private string GetUniqueCopyName(string baseName, IEnumerable<string> existingNames)
        {
            string newName = baseName + "_Copy";
            var set = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            if (!set.Contains(newName))
            {
                return newName;
            }
            int counter = 1;
            while (set.Contains($"{newName}_{counter}"))
            {
                counter++;
            }
            return $"{newName}_{counter}";
        }

        private void BtnDeleteSidebarItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            object item = btn.DataContext;
            if (item == null) return;

            string itemName = "";
            if (item is MacroTypical t) itemName = t.Name;
            else if (item is TypicalGroup g) itemName = g.Name;
            else if (item is Configurator c) itemName = c.Name;

            ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", $"æ‚¨ç¡®å®šè¦åˆ é™¤é¡¹ '{itemName}' å—ï¼Ÿ", () =>
            {
                bool wasSelected = (_selectedItem == item);

                if (item is MacroTypical typical) _db.MacroTypicals.Remove(typical);
                else if (item is TypicalGroup group) _db.TypicalGroups.Remove(group);
                else if (item is Configurator config) _db.Configurators.Remove(config);

                if (wasSelected)
                {
                    _selectedItem = null;
                }

                RefreshSidebar();
                if (!wasSelected && _selectedItem != null)
                {
                    LstSidebarItems.SelectedItem = _selectedItem;
                }
                else
                {
                    UpdateWorkspaceSelection();
                }
                MarkDirty();
            });
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", "æ‚¨ç¡®å®šè¦åˆ é™¤æ­¤é¡¹ç›®å—ï¼Ÿ", () =>
            {
                if (_selectedItem is MacroTypical typical) _db.MacroTypicals.Remove(typical);
                else if (_selectedItem is TypicalGroup group) _db.TypicalGroups.Remove(group);
                else if (_selectedItem is Configurator config) _db.Configurators.Remove(config);

                _selectedItem = null;
                RefreshSidebar();
                UpdateWorkspaceSelection();
                MarkDirty();
            });
        }

        // ==================== VARIABLES LOGIC ====================
        private void BindVariablesList(List<ConfigurationVariable> list)
        {
            ItrVariables.ItemsSource = null;
            ItrVariables.ItemsSource = list;
            if (GridVariablesEmptyState != null)
            {
                GridVariablesEmptyState.Visibility = (list == null || list.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnAddVariable_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            List<ConfigurationVariable> vars = null;
            if (_selectedItem is MacroTypical typical) vars = typical.Variables;
            else if (_selectedItem is TypicalGroup group) vars = group.Variables;

            if (vars != null)
            {
                var newVar = new ConfigurationVariable { Name = "New_Variable_" + (vars.Count + 1), DisplayName = "æ–°å˜é‡" };
                vars.Add(newVar);
                BindVariablesList(vars);
                MarkDirty();
                ShowToast($"å·²æˆåŠŸæ·»åŠ å˜é‡ '{newVar.Name}'");
            }
        }

        private void BtnDeleteVariable_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var varItem = btn.DataContext as ConfigurationVariable;
            if (varItem == null) return;

            List<ConfigurationVariable> vars = null;
            if (_selectedItem is MacroTypical typical) vars = typical.Variables;
            else if (_selectedItem is TypicalGroup group) vars = group.Variables;

            if (vars != null)
            {
                ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", $"ç¡®å®šåˆ é™¤å˜é‡ '{varItem.Name}' å—ï¼Ÿ", () =>
                {
                    vars.Remove(varItem);
                    BindVariablesList(vars);
                    MarkDirty();
                    ShowToast($"å·²åˆ é™¤å˜é‡ '{varItem.Name}'");
                });
            }
        }

        // ==================== ELEMENTS / MACROS LOGIC ====================
        private void BindElementsList(List<MacroElement> list)
        {
            ItrElements.ItemsSource = null;
            ItrElements.ItemsSource = list;
            if (TxtHeaderElements != null)
            {
                TxtHeaderElements.Text = $"Macros ({(list?.Count ?? 0)})";
            }
            if (GridElementsEmptyState != null)
            {
                GridElementsEmptyState.Visibility = (list == null || list.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnAddElement_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_selectedItem is MacroTypical typical)
            {
                ShowMacroNavigator(isAddingNew: true, target: null);
            }
        }

        private void BtnDeleteElement_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var elementItem = btn.DataContext as MacroElement;
            if (elementItem == null) return;

            if (_selectedItem is MacroTypical typical)
            {
                ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", "æ‚¨ç¡®å®šè¦åˆ é™¤æ­¤å®å…ƒç´ å—ï¼Ÿ", () =>
                {
                    typical.Elements.Remove(elementItem);
                    BindElementsList(typical.Elements);
                    MarkDirty();
                    ShowToast("å·²æˆåŠŸåˆ é™¤å®æ–‡ä»¶");
                });
            }
        }

        private void BtnReplaceElementMacro_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            ShowMacroNavigator(isAddingNew: false, target: element);
        }

        private void BtnShowElementPreview_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            char varChar = (char)('A' + element.VariantIndex);
            string msg = $"æ­£åœ¨æ˜¾ç¤ºå®é¢„è§ˆ:\n\nè·¯å¾„: {element.MacroPath}\nå˜ä½“: Variant {varChar}";
            MessageBox.Show(msg, "æ˜¾ç¤ºé¢„è§ˆ", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnShowElementRefs_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            if (string.IsNullOrEmpty(element.MacroPath))
            {
                MessageBox.Show("æ­¤å…ƒç´ å°šæœªæŒ‡å®šå®æ–‡ä»¶è·¯å¾„ã€‚", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var refs = new List<string>();
            foreach (var typical in _db.MacroTypicals)
            {
                if (typical.Elements.Any(el => string.Equals(el.MacroPath, element.MacroPath, StringComparison.OrdinalIgnoreCase)))
                {
                    refs.Add($"- å®è§„åˆ™: {typical.Name}");
                }
            }

            string msg = refs.Count > 0 
                ? $"å®æ–‡ä»¶ \"{element.MacroPath}\" è¢«ä»¥ä¸‹é¡¹ç›®å‚è€ƒ:\n\n{string.Join("\n", refs)}"
                : $"å®æ–‡ä»¶ \"{element.MacroPath}\" æ²¡æœ‰è¢«å…¶ä»–è§„åˆ™å‚è€ƒã€‚";

            MessageBox.Show(msg, "æ˜¾ç¤ºæ‰€æœ‰å­˜åœ¨å‚è€ƒ", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddElementBelow_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            if (_selectedItem is MacroTypical typical)
            {
                int index = typical.Elements.IndexOf(element);
                var newElement = new MacroElement();
                if (index != -1)
                {
                    typical.Elements.Insert(index + 1, newElement);
                }
                else
                {
                    typical.Elements.Add(newElement);
                }
                BindElementsList(typical.Elements);
                MarkDirty();
                ShowToast("å·²åœ¨ä¸‹æ–¹æ·»åŠ å®å…ƒç´ ");
            }
        }

        private void BtnCopyElement_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            if (_selectedItem is MacroTypical typical)
            {
                int index = typical.Elements.IndexOf(element);
                string json = JsonConvert.SerializeObject(element);
                var copy = JsonConvert.DeserializeObject<MacroElement>(json);
                if (index != -1)
                {
                    typical.Elements.Insert(index + 1, copy);
                }
                else
                {
                    typical.Elements.Add(copy);
                }
                BindElementsList(typical.Elements);
                MarkDirty();
                ShowToast("å·²å¤åˆ¶é€‰ä¸­çš„å®å…ƒç´ ");
            }
        }

        private void BtnBrowseElementMacro_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            var menu = new ContextMenu();
            var itemNav = new MenuItem { Header = "ðŸ” ä»Žé¡¹ç›®å®å¯¼èˆªå™¨é€‰æ‹©" };
            itemNav.Click += (s, ev) => ShowMacroNavigator(isAddingNew: false, target: element);
            
            var itemDisk = new MenuItem { Header = "ðŸ“‚ é€‰æ‹©æœ¬åœ°ç£ç›˜æ–‡ä»¶..." };
            itemDisk.Click += (s, ev) => {
                var dialog = new OpenFileDialog
                {
                    Filter = "EPLAN å®æ–‡ä»¶ (*.ema;*.ems;*.emp)|*.ema;*.ems;*.emp|æ‰€æœ‰æ–‡ä»¶ (*.*)|*.*",
                    Title = "é€‰æ‹©å®æ–‡ä»¶"
                };
                if (dialog.ShowDialog() == true)
                {
                    element.MacroPath = dialog.FileName;
                    element.VariantIndex = 0; // Reset variant

                    // Automatically extract placeholders
                    AutoExtractPlaceholdersForElement(element);

                    if (_selectedItem is MacroTypical typical)
                    {
                        BindElementsList(typical.Elements);
                    }
                    MarkDirty();
                }
            };
            menu.Items.Add(itemNav);
            menu.Items.Add(itemDisk);
            menu.IsOpen = true;
        }

        private void AutoExtractPlaceholdersForElement(MacroElement element)
        {
            if (element == null || string.IsNullOrWhiteSpace(element.MacroPath)) return;
            try
            {
                var placeholders = ConfiguratorService.ExtractPlaceholdersFromMacroFile(element.MacroPath, element.VariantIndex);
                if (placeholders != null && placeholders.Count > 0)
                {
                    var newMappings = new List<MappingEntry>();
                    foreach (var ph in placeholders)
                    {
                        var existing = element.VariableMappings?.FirstOrDefault(m => {
                            if (string.Equals(m.Key, ph, StringComparison.OrdinalIgnoreCase)) return true;
                            string normKey = m.Key.Replace(" : ", "=");
                            string normPh = ph.Replace(" : ", "=");
                            return string.Equals(normKey, normPh, StringComparison.OrdinalIgnoreCase);
                        });
                        newMappings.Add(new MappingEntry(ph, existing != null ? existing.Value : ""));
                    }
                    element.VariableMappings = newMappings;
                }
            }
            catch (Exception ex)
            {
                ConfiguratorService.LogDebug($"AutoExtractPlaceholdersForElement error: {ex.Message}");
            }
        }

        // ==================== MACRO NAVIGATOR PANEL LOGIC ====================
        private async void ShowMacroNavigator(bool isAddingNew, MacroElement target)
        {
            _isAddingNewElement = isAddingNew;
            _targetElementForMacroSelection = target;

            // Expand ColMacroNav column and show GridMacroNav & Splitter
            ColMacroNav.Width = new GridLength(280);
            GridMacroNav.Visibility = Visibility.Visible;
            if (SplitterMacroNav != null)
            {
                SplitterMacroNav.Visibility = Visibility.Visible;
            }

            // Only fetch if not already loaded to prevent slow reloading
            if (_allProjectMacros == null || _allProjectMacros.Count == 0)
            {
                await LoadMacrosFromActiveProjectAsync();
            }
        }

        private void BtnCloseMacroNav_Click(object sender, RoutedEventArgs e)
        {
            // Just fold/collapse the column and hide UI. DO NOT reset the macro cache.
            ColMacroNav.Width = new GridLength(0);
            GridMacroNav.Visibility = Visibility.Collapsed;
            if (SplitterMacroNav != null)
            {
                SplitterMacroNav.Visibility = Visibility.Collapsed;
            }
            _targetElementForMacroSelection = null;
            _isAddingNewElement = false;
        }

        private async void BtnRefreshMacroNav_Click(object sender, RoutedEventArgs e)
        {
            await LoadMacrosFromActiveProjectAsync();
        }

        private void TxtSearchMacro_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndShowMacros();
        }

        private void PopulateVariableMappings(MacroElement element, MacroTreeNode node)
        {
            if (element == null || node == null) return;

            var newMappings = new List<MappingEntry>();
            if (node.Children != null)
            {
                var phNames = node.Children
                    .Where(c => c.Type == "PlaceHolder" && !string.IsNullOrEmpty(c.Name))
                    .Select(c => c.Name)
                    .Distinct()
                    .ToList();

                foreach (var name in phNames)
                {
                    var existing = element.VariableMappings?.FirstOrDefault(m => {
                        if (string.Equals(m.Key, name, StringComparison.OrdinalIgnoreCase)) return true;
                        string normKey = m.Key.Replace(" : ", "=");
                        string normName = name.Replace(" : ", "=");
                        return string.Equals(normKey, normName, StringComparison.OrdinalIgnoreCase);
                    });
                    string value = existing != null ? existing.Value : "";
                    newMappings.Add(new MappingEntry(name, value));
                }
            }
            element.VariableMappings = newMappings;
        }

        private void TvMacros_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = TvMacros.SelectedItem as MacroTreeNode;
            if (selected == null) return;

            // Only allow selecting "Page" or "Variant" nodes
            if (selected.Type != "Page" && selected.Type != "Variant") return;

            if (_selectedItem is MacroTypical typical)
            {
                if (_isAddingNewElement)
                {
                    // Create new MacroElement with selected path and typical positioning settings
                    var newElement = new MacroElement
                    {
                        MacroPath = selected.FullPath,
                        VariantIndex = selected.VariantIndex,
                        PositionMode = "FromMacro",
                        IsActiveFormula = ""
                    };
                    PopulateVariableMappings(newElement, selected);
                    typical.Elements.Add(newElement);
                }
                else if (_targetElementForMacroSelection != null)
                {
                    // Update existing element
                    _targetElementForMacroSelection.MacroPath = selected.FullPath;
                    _targetElementForMacroSelection.VariantIndex = selected.VariantIndex;
                    PopulateVariableMappings(_targetElementForMacroSelection, selected);
                }

                // Force UI binding refresh
                BindElementsList(typical.Elements);
                MarkDirty();
            }

            // Close navigator panel after selection
            BtnCloseMacroNav_Click(sender, e);
        }

        private async Task LoadMacrosFromActiveProjectAsync()
        {
            if (ProgressBarLoad != null) ProgressBarLoad.Visibility = Visibility.Visible;
            if (BtnRefreshMacroNav != null) BtnRefreshMacroNav.IsEnabled = false;

            // Let the UI thread render the progress bar
            await Task.Delay(50);

            try
            {
                TxtSearchMacro.Text = "";
                _allProjectMacros = ConfiguratorService.GetActiveProjectMacros();
                FilterAndShowMacros();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"èŽ·å–æ´»åŠ¨é¡¹ç›®ä¸­çš„å®åˆ—è¡¨å¤±è´¥: {ex.Message}", "é…ç½®å™¨æç¤º", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (ProgressBarLoad != null) ProgressBarLoad.Visibility = Visibility.Collapsed;
                if (BtnRefreshMacroNav != null) BtnRefreshMacroNav.IsEnabled = true;
            }
        }

        private void FilterAndShowMacros()
        {
            string search = TxtSearchMacro.Text.Trim().ToLower();
            var filtered = _allProjectMacros.Where(m =>
                string.IsNullOrEmpty(search) ||
                (m.Name != null && m.Name.ToLower().Contains(search)) ||
                (m.Description != null && m.Description.ToLower().Contains(search)) ||
                (m.MacroPath != null && m.MacroPath.ToLower().Contains(search))
            ).ToList();

            TvMacros.ItemsSource = BuildMacroTree(filtered);
        }

        private List<MacroTreeNode> BuildMacroTree(List<EplanMacroInfo> macros)
        {
            var rootNodes = new List<MacroTreeNode>();

            // Root node: Defining
            var root = new MacroTreeNode
            {
                Name = "Defining",
                Type = "Root",
                Icon = "ðŸ—‚ï¸"
            };
            rootNodes.Add(root);

            foreach (var macro in macros)
            {
                string projName = string.IsNullOrEmpty(macro.ProjectName) ? "Without name" : macro.ProjectName;
                var projNode = root.Children.FirstOrDefault(n => n.Type == "Project" && n.Name == projName);
                if (projNode == null)
                {
                    projNode = new MacroTreeNode
                    {
                        Name = projName,
                        Type = "Project",
                        Icon = "ðŸ“"
                    };
                    root.Children.Add(projNode);
                }

                var pathParts = SplitMacroPath(macro.MacroPath);
                if (pathParts.Count == 0) continue;

                var folderParts = pathParts.Take(pathParts.Count - 1).ToList();
                string macroFileName = Path.GetFileNameWithoutExtension(pathParts.Last());

                var currentNode = projNode;
                foreach (var folder in folderParts)
                {
                    var folderNode = currentNode.Children.FirstOrDefault(n => n.Type == "Folder" && n.Name == folder);
                    if (folderNode == null)
                    {
                        folderNode = new MacroTreeNode
                        {
                            Name = folder,
                            Type = "Folder",
                            Icon = "ðŸ“"
                        };
                        currentNode.Children.Add(folderNode);
                    }
                    currentNode = folderNode;
                }

                var macroNode = currentNode.Children.FirstOrDefault(n => n.Type == "Macro" && n.Name == macroFileName);
                if (macroNode == null)
                {
                    macroNode = new MacroTreeNode
                    {
                        Name = macroFileName,
                        Type = "Macro",
                        Icon = "ðŸ“¦",
                        FullPath = macro.MacroPath
                    };
                    currentNode.Children.Add(macroNode);
                }

                if (macro.IsPageMacro)
                {
                    string pageDisplayName = macro.Name;

                    var pageNode = macroNode.Children.FirstOrDefault(n => n.Type == "Page" && n.Name == pageDisplayName);
                    if (pageNode == null)
                    {
                        pageNode = new MacroTreeNode
                        {
                            Name = pageDisplayName,
                            Type = "Page",
                            Icon = "ðŸ“„",
                            FullPath = macro.MacroPath,
                            VariantIndex = macro.VariantIndex,
                            RepresentationType = macro.RepresentationType
                        };
                        macroNode.Children.Add(pageNode);
                    }

                    if (macro.Placeholders != null)
                    {
                        foreach (var ph in macro.Placeholders)
                        {
                            string phName = string.IsNullOrEmpty(ph) ? "" : ph;
                            if (!pageNode.Children.Any(n => n.Type == "PlaceHolder" && n.Name == phName))
                            {
                                pageNode.Children.Add(new MacroTreeNode
                                {
                                    Name = phName,
                                    Type = "PlaceHolder",
                                    Icon = "âš“",
                                    FullPath = macro.MacroPath
                                });
                            }
                        }
                    }
                }
                else
                {
                    string repTypeName = GetRepresentationTypeName(macro.RepresentationType);
                    var repNode = macroNode.Children.FirstOrDefault(n => n.Type == "RepType" && n.Name == repTypeName);
                    if (repNode == null)
                    {
                        repNode = new MacroTreeNode
                        {
                            Name = repTypeName,
                            Type = "RepType",
                            Icon = "âš™ï¸",
                            FullPath = macro.MacroPath
                        };
                        macroNode.Children.Add(repNode);
                    }

                    string variantName = GetVariantName(macro.VariantIndex);
                    var variantNode = repNode.Children.FirstOrDefault(n => n.Type == "Variant" && n.Name == variantName);
                    if (variantNode == null)
                    {
                        variantNode = new MacroTreeNode
                        {
                            Name = variantName,
                            Type = "Variant",
                            Icon = "ðŸ”¹",
                            FullPath = macro.MacroPath,
                            VariantIndex = macro.VariantIndex,
                            RepresentationType = macro.RepresentationType
                        };
                        repNode.Children.Add(variantNode);
                    }

                    if (macro.Placeholders != null)
                    {
                        foreach (var ph in macro.Placeholders)
                        {
                            string phName = string.IsNullOrEmpty(ph) ? "" : ph;
                            if (!variantNode.Children.Any(n => n.Type == "PlaceHolder" && n.Name == phName))
                            {
                                variantNode.Children.Add(new MacroTreeNode
                                {
                                    Name = phName,
                                    Type = "PlaceHolder",
                                    Icon = "âš“",
                                    FullPath = macro.MacroPath
                                });
                            }
                        }
                    }
                }
            }

            return rootNodes;
        }

        private static List<string> SplitMacroPath(string macroPath)
        {
            string cleanPath = macroPath;
            if (cleanPath.StartsWith("$("))
            {
                int idx = cleanPath.IndexOf(')');
                if (idx != -1 && idx < cleanPath.Length - 1)
                {
                    cleanPath = cleanPath.Substring(idx + 1);
                }
            }
            cleanPath = cleanPath.TrimStart('\\', '/');
            var parts = cleanPath.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            return parts;
        }

        private static string GetVariantName(int index)
        {
            if (index >= 0 && index < 16)
            {
                return "Variant " + (char)('A' + index);
            }
            return "Variant A";
        }

        private static string GetRepresentationTypeName(int repType)
        {
            switch (repType)
            {
                case 1: return "Multi-line";
                case 2: return "Single-line";
                case 3: return "Pair cross-reference";
                case 4: return "Overview";
                case 5: return "Graphics";
                case 6: return "Panel layout";
                case 7: return "Fluid power schematic";
                case 8: return "P&ID";
                case 9: return "Pre-planning";
                case 10: return "Topology";
                case 11: return "3D mounting layout";
                case 12: return "Functional";
                default: return "Multi-line";
            }
        }

        public class MacroTreeNode
        {
            public string Name { get; set; }
            public string Type { get; set; } // "Root", "Project", "Folder", "Macro", "RepType", "Variant", "PlaceHolder"
            public string FullPath { get; set; }
            public string Icon { get; set; }
            public string Description { get; set; }
            public int VariantIndex { get; set; }
            public int RepresentationType { get; set; }
            public bool IsExpanded { get; set; } = true;
            public List<MacroTreeNode> Children { get; set; } = new List<MacroTreeNode>();
        }

        // ==================== PLACEHOLDER VARIABLES MAPPINGS ====================
        private void BtnAddVarMapping_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            
            // Find parent items control datacontext (which is MacroElement)
            DependencyObject parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is ContentPresenter))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent != null)
            {
                var element = ((ContentPresenter)parent).Content as MacroElement;
                if (element != null)
                {
                    element.VariableMappings.Add(new MappingEntry("æ–°å ä½ç¬¦", ""));
                    if (_selectedItem is MacroTypical typical)
                    {
                        BindElementsList(typical.Elements);
                    }
                }
            }
        }

        private void BtnDeleteVarMapping_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var entry = btn.DataContext as MappingEntry;
            if (entry == null) return;

            // Find parent ItemsControl datacontext (MacroElement)
            DependencyObject parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is Border))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent != null)
            {
                var element = ((Border)parent).DataContext as MacroElement;
                if (element != null)
                {
                    element.VariableMappings.Remove(entry);
                    if (_selectedItem is MacroTypical typical)
                    {
                        BindElementsList(typical.Elements);
                    }
                }
            }
        }

        // ==================== SAVE & GENERATE ====================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            MarkDirty();
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            MarkDirty();

            if (_selectedTab == 3)
            {
                if (!(_selectedItem is Configurator config))
                {
                    MessageBox.Show("è¯·å…ˆé€‰æ‹©ä¸€ä¸ª [é…ç½®å™¨] ä»¥ä¾¿è‡ªåŠ¨ç»˜å›¾ï¼", "è­¦å‘Š", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtStatus.Text = "æ­£åœ¨ç”ŸæˆåŽŸç†å›¾...";
                TxtStatus.Foreground = Brushes.Blue;

                var groupParameters = new Dictionary<string, Dictionary<string, object>>();
                foreach (var state in _builderStates)
                {
                    groupParameters[state.Group.Name] = GetGroupParameters(state);
                }

                Action generateAction = () =>
                {
                    string result = ConfiguratorService.GenerateConfigurator(config, _db, groupParameters);
                    
                    this.Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = result;
                        TxtStatus.Foreground = (result.StartsWith("é”™è¯¯") || result.StartsWith("å‘ç”Ÿå¼‚å¸¸")) ? Brushes.Red : Brushes.Green;
                    });
                };

                var logWindow = new GenerationLogWindow(this, generateAction);
                logWindow.ShowDialog();
            }
            else
            {
                if (!(_selectedItem is MacroTypical typical))
                {
                    MessageBox.Show("è¯·å…ˆé€‰æ‹©ä¸€ä¸ª [å®è§„åˆ™] ä»¥ä¾¿è‡ªåŠ¨ç»˜å›¾ï¼", "è­¦å‘Š", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtStatus.Text = "æ­£åœ¨è§£æžè§„åˆ™å¹¶æ‰¹é‡ç”Ÿæˆå›¾çº¸...";
                TxtStatus.Foreground = Brushes.Blue;

                Action generateAction = () =>
                {
                    string result = ConfiguratorService.GenerateTypical(typical);
                    
                    this.Dispatcher.Invoke(() =>
                    {
                        TxtStatus.Text = result;
                        TxtStatus.Foreground = (result.StartsWith("é”™è¯¯") || result.StartsWith("å‘ç”Ÿå¼‚å¸¸")) ? Brushes.Red : Brushes.Green;
                    });
                };

                var logWindow = new GenerationLogWindow(this, generateAction);
                logWindow.ShowDialog();
            }
        }

        // ==================== TYPICAL INSTANCES LOGIC ====================
        private void BindInstancesList(List<TypicalInstance> list)
        {
            ItrInstances.ItemsSource = null;
            ItrInstances.ItemsSource = list;
            if (TxtHeaderInstances != null)
            {
                TxtHeaderInstances.Text = $"Macro Typicals and Typical Groups ({(list?.Count ?? 0)})";
            }
            if (GridInstancesEmptyState != null)
            {
                GridInstancesEmptyState.Visibility = (list == null || list.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnAddInstance_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent Expander toggle!
            if (!(_selectedItem is TypicalGroup selectedGroup)) return;

            if (ModalAddElements == null || TxtModalAddTitle == null || TxtModalAddSubtitle == null || LstModalAddElements == null) return;

            TxtModalAddTitle.Text = "Add elements";
            TxtModalAddSubtitle.Text = "Select elements to be added to the Typical Group.";

            // Populate all available MacroTypicals and other TypicalGroups (excluding self)
            _allModalAddElements.Clear();
            
            foreach (var mt in _db.MacroTypicals)
            {
                _allModalAddElements.Add(new CheckableAddElement
                {
                    Name = mt.Name,
                    Type = "MacroTypical",
                    Icon = "ðŸ§¿", // Concentric circles icon
                    IsChecked = false
                });
            }

            foreach (var tg in _db.TypicalGroups)
            {
                if (string.Equals(tg.Name, selectedGroup.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Exclude self
                }
                _allModalAddElements.Add(new CheckableAddElement
                {
                    Name = tg.Name,
                    Type = "TypicalGroup",
                    Icon = "ðŸ‘¥", // Group icon
                    IsChecked = false
                });
            }

            TxtModalAddSearch.Text = "";
            FilterModalAddElements();

            ModalAddElements.Visibility = Visibility.Visible;
            UpdateModalAddSelectedCount();
        }

        private void BtnDeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var instance = btn.DataContext as TypicalInstance;
            if (instance == null) return;

            if (_selectedItem is TypicalGroup group)
            {
                ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", "æ‚¨ç¡®å®šè¦æ–­å¼€ä¸Žæ­¤è§„åˆ™å®žä¾‹çš„å…³è”å—ï¼Ÿ", () =>
                {
                    group.Instances.Remove(instance);
                    BindInstancesList(group.Instances);
                    MarkDirty();
                });
            }
        }

        private void CboTargetTypical_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cbo = sender as ComboBox;
            if (cbo == null) return;
            var instance = cbo.DataContext as TypicalInstance;
            if (instance == null) return;

            var targetInfo = cbo.SelectedItem as TypicalTargetInfo;
            if (targetInfo == null) return;

            instance.TargetName = targetInfo.Name;
            instance.Type = targetInfo.Type;

            // Automatically populate child variables mapping template
            List<ConfigurationVariable> childVars = null;
            if (instance.Type.Equals("MacroTypical", StringComparison.OrdinalIgnoreCase))
            {
                var child = _db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
                childVars = child?.Variables;
            }
            else if (instance.Type.Equals("TypicalGroup", StringComparison.OrdinalIgnoreCase))
            {
                var child = _db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
                childVars = child?.Variables;
            }

            if (childVars != null)
            {
                var newMappings = new List<MappingEntry>();
                foreach (var v in childVars)
                {
                    var existing = instance.VariableMappings?.FirstOrDefault(m => string.Equals(m.Key, v.Name, StringComparison.OrdinalIgnoreCase));
                    string val = existing != null ? existing.Value : "";
                    newMappings.Add(new MappingEntry(v.Name, val));
                }
                instance.VariableMappings = newMappings;
            }
            else
            {
                instance.VariableMappings = new List<MappingEntry>();
            }

            // Refresh UI
            if (_selectedItem is TypicalGroup group)
            {
                BindInstancesList(group.Instances);
                MarkDirty();
            }
        }

        public List<TypicalTargetInfo> AllTypicalTargets
        {
            get
            {
                var list = new List<TypicalTargetInfo>();
                if (_db?.MacroTypicals != null)
                {
                    foreach (var t in _db.MacroTypicals)
                    {
                        list.Add(new TypicalTargetInfo { Name = t.Name, Type = "MacroTypical" });
                    }
                }
                if (_db?.TypicalGroups != null)
                {
                    foreach (var g in _db.TypicalGroups)
                    {
                        if (_selectedItem is TypicalGroup selectedGroup && string.Equals(g.Name, selectedGroup.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Prevent self-nesting
                        }
                        list.Add(new TypicalTargetInfo { Name = g.Name, Type = "TypicalGroup" });
                    }
                }
                return list;
            }
        }

        // ==================== CONFIGURATOR DETAILS LOGIC ====================
        private void TxtConfigDescription_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_selectedItem is Configurator config)
            {
                config.Description = TxtConfigDescription.Text;
                MarkDirty();
            }
        }

        private void BindConfigGroupsList(Configurator config)
        {
            ItrConfigGroupsList.ItemsSource = null;
            if (config != null)
            {
                ItrConfigGroupsList.ItemsSource = config.TypicalGroupRefs;
                if (TxtHeaderConfigGroups != null)
                {
                    TxtHeaderConfigGroups.Text = $"Associated Rule Groups ({(config.TypicalGroupRefs?.Count ?? 0)})";
                }
                if (GridConfigGroupsEmptyState != null)
                {
                    GridConfigGroupsEmptyState.Visibility = (config.TypicalGroupRefs == null || config.TypicalGroupRefs.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void BtnDeleteConfigGroup_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            string groupName = btn.DataContext as string;
            if (string.IsNullOrEmpty(groupName)) return;

            if (_selectedItem is Configurator config)
            {
                ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", $"ç¡®å®šè¦æ–­å¼€è§„åˆ™ç»„ '{groupName}' çš„å…³è”å—ï¼Ÿ", () =>
                {
                    config.TypicalGroupRefs.Remove(groupName);
                    BindConfigGroupsList(config);
                    MarkDirty();
                    ShowToast($"å·²æ–­å¼€è§„åˆ™ç»„ '{groupName}' çš„å…³è”");
                });
            }
        }

        // ==================== PROJECT BUILDER FORM LOGIC ====================
        private class GroupBuilderFormState
        {
            public TypicalGroup Group { get; set; }
            public Dictionary<string, FrameworkElement> ControlMap { get; set; }
            public Dictionary<string, FrameworkElement> RowMap { get; set; }
        }
        private List<GroupBuilderFormState> _builderStates = new List<GroupBuilderFormState>();

        private void RenderProjectBuilderForm(Configurator runConfig)
        {
            PanelBuilderForm.Children.Clear();
            _builderStates.Clear();

            if (runConfig.TypicalGroupRefs == null || runConfig.TypicalGroupRefs.Count == 0)
            {
                var emptyMsg = new TextBlock
                {
                    Text = "æ­¤é…ç½®å™¨å°šæœªå…³è”ä»»ä½•è§„åˆ™ç»„ï¼è¯·åœ¨è®¾è®¡æ¨¡å¼ä¸‹å…³è”è§„åˆ™ç»„åŽå†è¿›è¡Œç”Ÿæˆã€‚",
                    Foreground = Brushes.Red,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                PanelBuilderForm.Children.Add(emptyMsg);
                return;
            }

            foreach (string groupRef in runConfig.TypicalGroupRefs)
            {
                var group = _db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, groupRef, StringComparison.OrdinalIgnoreCase));
                if (group == null) continue;

                var state = new GroupBuilderFormState
                {
                    Group = group,
                    ControlMap = new Dictionary<string, FrameworkElement>(),
                    RowMap = new Dictionary<string, FrameworkElement>()
                };

                // Group Title
                var header = new TextBlock
                {
                    Text = $"è§„åˆ™ç»„: {group.Name}",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    Margin = new Thickness(0, 15, 0, 10)
                };
                PanelBuilderForm.Children.Add(header);

                var groupPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 15) };

                if (group.Variables == null || group.Variables.Count == 0)
                {
                    var noVars = new TextBlock
                    {
                        Text = "è¯¥è§„åˆ™ç»„æ— é…ç½®å˜é‡",
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(10, 5, 0, 5)
                    };
                    groupPanel.Children.Add(noVars);
                }
                else
                {
                    // Group variables by category
                    var categories = group.Variables
                        .GroupBy(v => string.IsNullOrWhiteSpace(v.Category) ? "å¸¸è§„å‚æ•°" : v.Category.Trim())
                        .ToList();

                    foreach (var cat in categories)
                    {
                        // Create an Expander for this category
                        var expander = new Expander
                        {
                            Header = cat.Key,
                            IsExpanded = true,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 10),
                            Background = Brushes.Transparent,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch
                        };

                        var catPanel = new StackPanel { Margin = new Thickness(5) };

                        foreach (var variable in cat)
                        {
                            var rowGrid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            var label = new Label
                            {
                                Content = string.IsNullOrEmpty(variable.DisplayName) ? variable.Name : variable.DisplayName,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 10, 0),
                                FontWeight = FontWeights.Normal
                            };
                            Grid.SetColumn(label, 0);
                            rowGrid.Children.Add(label);

                            FrameworkElement inputCtrl = null;
                            string defaultVal = variable.DefaultValue ?? "";

                            if (variable.PredefinedValues != null && variable.PredefinedValues.Count > 0)
                            {
                                var cbo = new ComboBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
                                foreach (var opt in variable.PredefinedValues)
                                {
                                    cbo.Items.Add(opt);
                                }

                                int defIdx = variable.PredefinedValues.IndexOf(defaultVal);
                                cbo.SelectedIndex = defIdx != -1 ? defIdx : 0;

                                cbo.SelectionChanged += (s, ev) => EvaluateVisibilityForGroup(state);
                                inputCtrl = cbo;
                            }
                            else if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                            {
                                var chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                                bool.TryParse(defaultVal, out bool defBool);
                                chk.IsChecked = defBool;

                                chk.Checked += (s, ev) => EvaluateVisibilityForGroup(state);
                                chk.Unchecked += (s, ev) => EvaluateVisibilityForGroup(state);
                                inputCtrl = chk;
                            }
                            else
                            {
                                var txt = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
                                txt.Text = defaultVal;

                                txt.TextChanged += (s, ev) => EvaluateVisibilityForGroup(state);
                                inputCtrl = txt;
                            }

                            Grid.SetColumn(inputCtrl, 1);
                            rowGrid.Children.Add(inputCtrl);

                            state.ControlMap[variable.Name] = inputCtrl;
                            state.RowMap[variable.Name] = rowGrid;
                            catPanel.Children.Add(rowGrid);
                        }

                        expander.Content = catPanel;
                        groupPanel.Children.Add(expander);
                    }
                }

                PanelBuilderForm.Children.Add(groupPanel);
                _builderStates.Add(state);

                // Initial visibility run
                EvaluateVisibilityForGroup(state);
            }
        }

        private void EvaluateVisibilityForGroup(GroupBuilderFormState state)
        {
            var parameters = GetGroupParameters(state);
            foreach (var variable in state.Group.Variables)
            {
                if (state.RowMap.TryGetValue(variable.Name, out var rowGrid))
                {
                    if (string.IsNullOrWhiteSpace(variable.VisibilityFormula))
                    {
                        rowGrid.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        bool visible = ConfiguratorService.EvaluateActiveFormula(variable.VisibilityFormula, parameters);
                        rowGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
            ValidateProjectBuilderForm();
        }

        private Dictionary<string, object> GetGroupParameters(GroupBuilderFormState state)
        {
            var parameters = new Dictionary<string, object>();
            foreach (var variable in state.Group.Variables)
            {
                if (state.ControlMap.TryGetValue(variable.Name, out var ctrl))
                {
                    object val = null;
                    if (ctrl is ComboBox cbo)
                    {
                        val = cbo.SelectedItem?.ToString() ?? "";
                    }
                    else if (ctrl is CheckBox chk)
                    {
                        val = chk.IsChecked == true;
                    }
                    else if (ctrl is TextBox txt)
                    {
                        string textVal = txt.Text;
                        val = textVal;
                        if (variable.Type.Equals("Integer", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(textVal, out int intVal)) val = intVal;
                        }
                        else if (variable.Type.Equals("Double", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(textVal, out double doubleVal)) val = doubleVal;
                        }
                    }
                    parameters[variable.Name] = val;
                }
                else
                {
                    parameters[variable.Name] = null;
                }
            }
            return parameters;
        }

        private void BtnVariablePicker_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Parent is Grid parentGrid)
            {
                TextBox targetTextBox = null;
                foreach (var child in parentGrid.Children)
                {
                    if (child is TextBox txt)
                    {
                        targetTextBox = txt;
                        break;
                    }
                }

                if (targetTextBox == null) return;

                var menu = new ContextMenu();

                // 1. Available Configuration Variables from current context
                List<string> varNames = new List<string>();
                if (_selectedItem is MacroTypical typical)
                {
                    varNames.AddRange(typical.Variables.Select(v => v.Name));
                }
                else if (_selectedItem is TypicalGroup group)
                {
                    varNames.AddRange(group.Variables.Select(v => v.Name));
                }

                if (varNames.Count > 0)
                {
                    var varsHeader = new MenuItem { Header = "é…ç½®å˜é‡ (Variables)", IsEnabled = false };
                    menu.Items.Add(varsHeader);
                    foreach (var name in varNames)
                    {
                        var item = new MenuItem { Header = name };
                        item.Click += (s, ev) => InsertTextIntoTextBox(targetTextBox, name);
                        menu.Items.Add(item);
                    }
                    menu.Items.Add(new Separator());
                }

                // 2. Add common formulas & helper functions
                var formulasHeader = new MenuItem { Header = "å…¬å¼å‡½æ•° (Functions)", IsEnabled = false };
                menu.Items.Add(formulasHeader);

                var lookupItem = new MenuItem { Header = "æ–­è·¯å™¨é€‰åž‹: Lookup_Breaker('breaker_catalog.csv', 'OrderNumber', Current, 3)" };
                lookupItem.Click += (s, ev) => InsertTextIntoTextBox(targetTextBox, "Lookup_Breaker('breaker_catalog.csv', 'OrderNumber', Current, 3)");
                menu.Items.Add(lookupItem);

                var concatItem = new MenuItem { Header = "æ‹¼æŽ¥å­—ç¬¦ä¸²: Concat(str1, str2)" };
                concatItem.Click += (s, ev) => InsertTextIntoTextBox(targetTextBox, "Concat(A, B)");
                menu.Items.Add(concatItem);

                var ifItem = new MenuItem { Header = "æ¡ä»¶é€‰æ‹©: if(cond, trueVal, falseVal)" };
                ifItem.Click += (s, ev) => InsertTextIntoTextBox(targetTextBox, "if(Current > 100, '3P', '1P')");
                menu.Items.Add(ifItem);

                btn.ContextMenu = menu;
                menu.IsOpen = true;
            }
        }

        private void InsertTextIntoTextBox(TextBox txt, string text)
        {
            if (txt == null) return;
            int selectionStart = txt.SelectionStart;
            txt.Text = txt.Text.Remove(selectionStart, txt.SelectionLength).Insert(selectionStart, text);
            txt.SelectionStart = selectionStart + text.Length;
            txt.Focus();
        }

        private void FormulaTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox txt)
            {
                string formula = txt.Text;
                if (string.IsNullOrWhiteSpace(formula))
                {
                    txt.ClearValue(Control.BackgroundProperty);
                    txt.ToolTip = null;
                    return;
                }

                List<string> availableVars = new List<string>();
                if (_selectedItem is MacroTypical typical)
                {
                    availableVars.AddRange(typical.Variables.Select(v => v.Name));
                }
                else if (_selectedItem is TypicalGroup group)
                {
                    availableVars.AddRange(group.Variables.Select(v => v.Name));
                }

                string error = ConfiguratorService.ValidateExpression(formula, availableVars);
                if (error != null)
                {
                    txt.Background = new SolidColorBrush(Color.FromRgb(63, 34, 34)); // Dark red for errors in dark theme
                    txt.ToolTip = error;
                }
                else
                {
                    txt.Background = new SolidColorBrush(Color.FromRgb(34, 63, 34)); // Dark green for success in dark theme
                    txt.ToolTip = "å…¬å¼æœ‰æ•ˆ";
                }
            }
        }

        private void BtnExtractPlaceholders_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MacroElement element)
            {
                if (string.IsNullOrWhiteSpace(element.MacroPath))
                {
                    MessageBox.Show("è¯·å…ˆæŒ‡å®šå®æ–‡ä»¶è·¯å¾„ï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var placeholders = ConfiguratorService.ExtractPlaceholdersFromMacroFile(element.MacroPath, element.VariantIndex);
                    if (placeholders == null || placeholders.Count == 0)
                    {
                        MessageBox.Show("æœªæå–åˆ°ä»»ä½•å ä½ç¬¦å˜é‡ã€‚è¯·ç¡®ä¿å®æ–‡ä»¶å­˜åœ¨ä¸”åŒ…å«å ä½ç¬¦ï¼Œä¸”å½“å‰åœ¨EPLANä¸­å·²æ‰“å¼€äº†ä¸€ä¸ªé¡¹ç›®ã€‚", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var currentMappings = element.VariableMappings.ToDictionary(m => m.Key, m => m.Value);
                    element.VariableMappings.Clear();

                    foreach (var ph in placeholders)
                    {
                        string val = "";
                        var matchedKey = currentMappings.Keys.FirstOrDefault(k => 
                            string.Equals(k, ph, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(k.Replace(" : ", "="), ph.Replace(" : ", "="), StringComparison.OrdinalIgnoreCase)
                        );
                        if (matchedKey != null)
                        {
                            val = currentMappings[matchedKey];
                        }
                        element.VariableMappings.Add(new MappingEntry(ph, val));
                    }

                    if (_selectedItem is MacroTypical typical)
                    {
                        BindElementsList(typical.Elements);
                    }
                    MarkDirty();

                    MessageBox.Show($"æˆåŠŸæå– {placeholders.Count} ä¸ªå ä½ç¬¦å˜é‡ï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"æå–å ä½ç¬¦å¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "å¯¼å…¥è§„åˆ™åº“é…ç½®"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var importedData = JsonConvert.DeserializeObject<ConfiguratorData>(json);
                    if (importedData == null)
                    {
                        MessageBox.Show("å¯¼å…¥çš„æ–‡ä»¶æ ¼å¼ä¸æ­£ç¡®æˆ–ä¸ºç©ºï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var result = MessageBox.Show(
                        "æ˜¯å¦è¦†ç›–å½“å‰åº“çš„é…ç½®ï¼Ÿ\né€‰æ‹© [æ˜¯] å°†å®Œå…¨è¦†ç›–çŽ°æœ‰é…ç½®ã€‚\né€‰æ‹© [å¦] å°†åˆå¹¶æ–°é…ç½®ï¼ˆæ ¹æ®åç§°é¿å…é‡å¤ï¼‰ã€‚",
                        "å¯¼å…¥é€‰é¡¹",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel) return;

                    if (result == MessageBoxResult.Yes)
                    {
                        _db = importedData;
                    }
                    else
                    {
                        foreach (var mt in importedData.MacroTypicals)
                        {
                            if (!_db.MacroTypicals.Any(t => t.Name == mt.Name))
                                _db.MacroTypicals.Add(mt);
                        }
                        foreach (var tg in importedData.TypicalGroups)
                        {
                            if (!_db.TypicalGroups.Any(g => g.Name == tg.Name))
                                _db.TypicalGroups.Add(tg);
                        }
                        foreach (var cfg in importedData.Configurators)
                        {
                            if (!_db.Configurators.Any(c => c.Name == cfg.Name))
                                _db.Configurators.Add(cfg);
                        }
                    }

                    MarkDirty();
                    RefreshSidebar();
                    UpdateWorkspaceSelection();

                    MessageBox.Show("è§„åˆ™åº“å¯¼å…¥æˆåŠŸï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"å¯¼å…¥å¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "configurator_library.json",
                Title = "å¯¼å‡ºè§„åˆ™åº“é…ç½®"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(_db, Formatting.Indented);
                    File.WriteAllText(saveFileDialog.FileName, json);
                    MessageBox.Show("è§„åˆ™åº“å¯¼å‡ºæˆåŠŸï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"å¯¼å‡ºå¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLibrarySettings_Click(object sender, RoutedEventArgs e)
        {
            if (_db.Settings == null)
            {
                _db.Settings = new LibrarySettings();
            }

            var dialog = new LibrarySettingsDialog(_db.Settings);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _db.Settings = dialog.Settings;
                MarkDirty();
                MessageBox.Show("åº“å±žæ€§è®¾ç½®å·²ä¿å­˜ï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnImportBuilderConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                Title = "å¯¼å…¥ Project Builder å˜é‡é…ç½®"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string configuratorName = "";
                Dictionary<string, string> importedValues = null;

                if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    importedValues = ImportVariablesFromXml(filePath, out configuratorName);
                }
                else if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    importedValues = ImportVariablesFromXlsx(filePath, out configuratorName);
                }

                if (importedValues == null || importedValues.Count == 0)
                {
                    MessageBox.Show("æœªèƒ½åœ¨å¯¼å…¥çš„æ–‡ä»¶ä¸­æ‰¾åˆ°ä»»ä½•æœ‰æ•ˆçš„å˜é‡é…ç½®ï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Fill values into current form
                int filledCount = 0;
                foreach (var state in _builderStates)
                {
                    foreach (var variable in state.Group.Variables)
                    {
                        string val = null;
                        if (importedValues.TryGetValue($"{state.Group.Name}.{variable.Name}", out string v1))
                        {
                            val = v1;
                        }
                        else if (importedValues.TryGetValue(variable.Name, out string v2))
                        {
                            val = v2;
                        }

                        if (val != null)
                        {
                            if (state.ControlMap.TryGetValue(variable.Name, out var ctrl))
                            {
                                if (ctrl is TextBox txt)
                                {
                                    txt.Text = val;
                                    filledCount++;
                                }
                                else if (ctrl is ComboBox cbo)
                                {
                                    cbo.SelectedItem = val;
                                    cbo.Text = val;
                                    filledCount++;
                                }
                                else if (ctrl is CheckBox chk)
                                {
                                    chk.IsChecked = (string.Equals(val, "True", StringComparison.OrdinalIgnoreCase) || val == "1");
                                    filledCount++;
                                }
                            }
                        }
                    }
                }

                // Force visibility and validation refresh
                foreach (var state in _builderStates)
                {
                    EvaluateVisibilityForGroup(state);
                }
                ValidateProjectBuilderForm();

                MessageBox.Show($"æˆåŠŸå¯¼å…¥å¹¶å¡«å……äº† {filledCount} ä¸ªé…ç½®å˜é‡çš„å€¼ï¼", "å¯¼å…¥æˆåŠŸ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExportBuilderConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!(_selectedItem is Configurator config)) return;

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|Excel files (*.xlsx)|*.xlsx",
                FileName = $"{config.Name}_Config",
                Title = "å¯¼å‡º Project Builder å˜é‡é…ç½®"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;
                var values = new Dictionary<string, string>();

                // Gather current UI values
                foreach (var state in _builderStates)
                {
                    foreach (var variable in state.Group.Variables)
                    {
                        if (state.ControlMap.TryGetValue(variable.Name, out var ctrl))
                        {
                            string val = "";
                            if (ctrl is TextBox txt) val = txt.Text;
                            else if (ctrl is ComboBox cbo) val = cbo.SelectedItem?.ToString() ?? cbo.Text;
                            else if (ctrl is CheckBox chk) val = (chk.IsChecked == true).ToString();

                            values[variable.Name] = val;
                            values[$"{state.Group.Name}.{variable.Name}"] = val;
                        }
                    }
                }

                if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    ExportVariablesToXml(filePath, config.Name, values);
                }
                else if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    ExportVariablesToXlsx(filePath, config.Name, values);
                }

                MessageBox.Show("é…ç½®å˜é‡å¯¼å‡ºæˆåŠŸï¼", "æç¤º", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private Dictionary<string, string> ImportVariablesFromXml(string filePath, out string configuratorName)
        {
            var values = new Dictionary<string, string>();
            configuratorName = "";
            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root != null && root.Name.LocalName == "Configuration")
                {
                    configuratorName = root.Attribute("typical")?.Value ?? "";

                    // Read flat variables
                    var varsElement = root.Element("ConfigurationVariables");
                    if (varsElement != null)
                    {
                        foreach (var el in varsElement.Elements("ConfigurationVariable"))
                        {
                            string name = el.Attribute("name")?.Value ?? el.Name.LocalName;
                            string val = el.Value;
                            if (!string.IsNullOrEmpty(name))
                            {
                                values[name] = val;
                            }
                        }
                    }

                    // Read typical instances (nesting)
                    foreach (var instanceEl in root.Elements("TypicalInstance"))
                    {
                        string groupName = instanceEl.Attribute("name")?.Value ?? "";
                        foreach (var inst in instanceEl.Elements("Instance"))
                        {
                            var subVars = inst.Element("ConfigurationVariables");
                            if (subVars != null)
                            {
                                foreach (var el in subVars.Elements("ConfigurationVariable"))
                                {
                                    string name = el.Attribute("name")?.Value ?? el.Name.LocalName;
                                    string val = el.Value;
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        values[name] = val;
                                        if (!string.IsNullOrEmpty(groupName))
                                        {
                                            values[$"{groupName}.{name}"] = val;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"è§£æž XML æ–‡ä»¶å¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return values;
        }

        private void ExportVariablesToXml(string filePath, string configuratorName, Dictionary<string, string> values)
        {
            try
            {
                var root = new XElement("Configuration", new XAttribute("typical", configuratorName));

                // Write flat variables
                var varsElement = new XElement("ConfigurationVariables");
                foreach (var kvp in values)
                {
                    if (kvp.Key.Contains(".")) continue;
                    varsElement.Add(new XElement("ConfigurationVariable",
                        new XAttribute("name", kvp.Key),
                        kvp.Value));
                }
                root.Add(varsElement);

                // Write typical instances
                var groups = values.Keys.Where(k => k.Contains(".")).Select(k => k.Split('.')[0]).Distinct();
                foreach (var groupName in groups)
                {
                    var instanceEl = new XElement("TypicalInstance", new XAttribute("name", groupName));
                    var inst = new XElement("Instance");
                    var subVars = new XElement("ConfigurationVariables");

                    foreach (var kvp in values)
                    {
                        if (kvp.Key.StartsWith(groupName + "."))
                        {
                            string varName = kvp.Key.Substring(groupName.Length + 1);
                            subVars.Add(new XElement("ConfigurationVariable",
                                new XAttribute("name", varName),
                                kvp.Value));
                        }
                    }
                    inst.Add(subVars);
                    instanceEl.Add(inst);
                    root.Add(instanceEl);
                }

                var doc = new XDocument(root);
                doc.Save(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"å¯¼å‡º XML æ–‡ä»¶å¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Dictionary<string, string> ImportVariablesFromXlsx(string filePath, out string configuratorName)
        {
            var values = new Dictionary<string, string>();
            configuratorName = "";
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var sheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (sheet != null)
                    {
                        string a1Val = sheet.Cells[1, 1].Text ?? "";
                        if (a1Val.Contains("{"))
                        {
                            int idx = a1Val.IndexOf('{');
                            configuratorName = a1Val.Substring(0, idx).Trim();
                        }
                        else
                        {
                            configuratorName = a1Val.Trim();
                        }

                        int rowCount = sheet.Dimension?.End.Row ?? 0;
                        string currentGroup = "";
                        List<string> groupHeaders = new List<string>();

                        for (int row = 2; row <= rowCount; row++)
                        {
                            string colA = sheet.Cells[row, 1].Text?.Trim() ?? "";
                            string colB = sheet.Cells[row, 2].Text?.Trim() ?? "";

                            if (string.IsNullOrEmpty(colA) && string.IsNullOrEmpty(colB))
                            {
                                currentGroup = "";
                                groupHeaders.Clear();
                                continue;
                            }

                            if (string.IsNullOrEmpty(currentGroup) && !string.IsNullOrEmpty(colA) && string.IsNullOrEmpty(colB))
                            {
                                int colCount = sheet.Dimension.End.Column;
                                bool isHeader = false;
                                for (int col = 2; col <= colCount; col++)
                                {
                                    if (!string.IsNullOrEmpty(sheet.Cells[row, col].Text))
                                    {
                                        isHeader = true;
                                        break;
                                    }
                                }

                                if (isHeader)
                                {
                                    currentGroup = colA;
                                    for (int col = 2; col <= colCount; col++)
                                    {
                                        groupHeaders.Add(sheet.Cells[row, col].Text?.Trim() ?? "");
                                    }
                                    continue;
                                }
                            }

                            if (!string.IsNullOrEmpty(currentGroup))
                            {
                                int colCount = sheet.Dimension.End.Column;
                                for (int col = 2; col <= Math.Min(colCount, groupHeaders.Count + 1); col++)
                                {
                                    string header = groupHeaders[col - 2];
                                    string val = sheet.Cells[row, col].Text?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(header))
                                    {
                                        values[header] = val;
                                        values[$"{currentGroup}.{header}"] = val;
                                    }
                                }
                            }
                            else
                            {
                                string varName = colA;
                                if (colA.Contains("{") && colA.Contains("}"))
                                {
                                    int start = colA.LastIndexOf('{');
                                    int end = colA.LastIndexOf('}');
                                    if (end > start)
                                    {
                                        varName = colA.Substring(start + 1, end - start - 1).Trim();
                                    }
                                }
                                if (!string.IsNullOrEmpty(varName))
                                {
                                    values[varName] = colB;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"è§£æž Excel æ–‡ä»¶å¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return values;
        }

        private void ExportVariablesToXlsx(string filePath, string configuratorName, Dictionary<string, string> values)
        {
            try
            {
                using (var package = new ExcelPackage())
                {
                    var sheet = package.Workbook.Worksheets.Add("Configuration");

                    sheet.Cells[1, 1].Value = configuratorName;
                    sheet.Cells[1, 1].Style.Font.Bold = true;

                    int row = 2;
                    foreach (var kvp in values)
                    {
                        if (kvp.Key.Contains(".")) continue;
                        sheet.Cells[row, 1].Value = kvp.Key;
                        sheet.Cells[row, 2].Value = kvp.Value;
                        row++;
                    }

                    var groups = values.Keys.Where(k => k.Contains(".")).Select(k => k.Split('.')[0]).Distinct().ToList();
                    foreach (var groupName in groups)
                    {
                        row++;

                        sheet.Cells[row, 1].Value = groupName;
                        sheet.Cells[row, 1].Style.Font.Bold = true;

                        var groupVars = values.Keys.Where(k => k.StartsWith(groupName + ".")).Select(k => k.Substring(groupName.Length + 1)).ToList();
                        for (int i = 0; i < groupVars.Count; i++)
                        {
                            sheet.Cells[row, i + 2].Value = groupVars[i];
                            sheet.Cells[row, i + 2].Style.Font.Bold = true;
                        }
                        row++;

                        sheet.Cells[row, 1].Value = groupName;
                        for (int i = 0; i < groupVars.Count; i++)
                        {
                            string key = $"{groupName}.{groupVars[i]}";
                            sheet.Cells[row, i + 2].Value = values[key];
                        }
                        row++;
                    }

                    sheet.Cells.AutoFitColumns();
                    package.SaveAs(new FileInfo(filePath));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"å¯¼å‡º Excel æ–‡ä»¶å¤±è´¥: {ex.Message}", "é”™è¯¯", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ValidateProjectBuilderForm()
        {
            if (_selectedTab != 3) return;
            bool isValid = true;
            string firstError = null;

            foreach (var state in _builderStates)
            {
                foreach (var variable in state.Group.Variables)
                {
                    if (variable.IsMandatory)
                    {
                        if (state.RowMap.TryGetValue(variable.Name, out var rowGrid) && rowGrid.Visibility == Visibility.Visible)
                        {
                            if (state.ControlMap.TryGetValue(variable.Name, out var ctrl))
                            {
                                string val = "";
                                if (ctrl is TextBox txt) val = txt.Text.Trim();
                                else if (ctrl is ComboBox cbo) val = cbo.SelectedItem?.ToString() ?? "";
                                else if (ctrl is CheckBox chk) val = (chk.IsChecked == true).ToString();

                                if (string.IsNullOrEmpty(val))
                                {
                                    isValid = false;
                                    if (firstError == null)
                                    {
                                        firstError = $"å¿…å¡«å‚æ•° [{variable.DisplayName ?? variable.Name}] ä¸èƒ½ä¸ºç©ºï¼";
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (!isValid)
            {
                BtnGenerate.IsEnabled = false;
                TxtStatus.Text = firstError;
                TxtStatus.Foreground = Brushes.Red;
            }
            else
            {
                BtnGenerate.IsEnabled = true;
                TxtStatus.Text = "å°±ç»ª";
                TxtStatus.Foreground = Brushes.Green;
            }
        }

        // ==================== EBUILD STYLE INTERACTIONS & TRIGGERS ====================
        private void ItrElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedMacroElement = ItrElements.SelectedItem as MacroElement;
            if (_selectedMacroElement != null)
            {
                ItrVariables.SelectionChanged -= ItrVariables_SelectionChanged;
                ItrVariables.SelectedItem = null;
                ItrVariables.SelectionChanged += ItrVariables_SelectionChanged;

                if (ItrInstances != null)
                {
                    ItrInstances.SelectionChanged -= ItrInstances_SelectionChanged;
                    ItrInstances.SelectedItem = null;
                    ItrInstances.SelectionChanged += ItrInstances_SelectionChanged;
                }

                _selectedConfigVariable = null;
                GridVariableDetails.Visibility = Visibility.Collapsed;
                if (GridInstanceDetails != null) GridInstanceDetails.Visibility = Visibility.Collapsed;

                GridDetailsEmpty.Visibility = Visibility.Collapsed;
                GridMacroDetails.Visibility = Visibility.Visible;
                GridMacroDetails.DataContext = null;
                GridMacroDetails.DataContext = _selectedMacroElement;

                // Bind Active Formula Status radio buttons
                if (string.IsNullOrEmpty(_selectedMacroElement.IsActiveFormula))
                {
                    RadStatusAlways.IsChecked = true;
                    if (TxtIsActiveFormula != null) TxtIsActiveFormula.IsEnabled = false;
                }
                else
                {
                    RadStatusDependent.IsChecked = true;
                    if (TxtIsActiveFormula != null) TxtIsActiveFormula.IsEnabled = true;
                }

                // Bind Position Mode radio buttons
                if (string.Equals(_selectedMacroElement.PositionMode, "FromMacro", StringComparison.OrdinalIgnoreCase))
                {
                    RadPosMacro.IsChecked = true;
                    ChkPosSequential.IsChecked = false;
                }
                else if (string.Equals(_selectedMacroElement.PositionMode, "Absolute", StringComparison.OrdinalIgnoreCase))
                {
                    RadPosAbsolute.IsChecked = true;
                    ChkPosSequential.IsChecked = false;
                }
                else if (string.Equals(_selectedMacroElement.PositionMode, "Sequential", StringComparison.OrdinalIgnoreCase))
                {
                    RadPosAbsolute.IsChecked = true;
                    ChkPosSequential.IsChecked = true;
                }

                // Bind Structure checkboxes
                ChkStructureMacro.IsChecked = _selectedMacroElement.StructureMappings == null || _selectedMacroElement.StructureMappings.All(m => string.IsNullOrEmpty(m.Value));
            }
            else
            {
                GridMacroDetails.Visibility = Visibility.Collapsed;
                if (_selectedConfigVariable == null && GridInstanceDetails?.Visibility != Visibility.Visible)
                {
                    GridDetailsEmpty.Visibility = Visibility.Visible;
                }
            }
        }

        private void ItrVariables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedConfigVariable = ItrVariables.SelectedItem as ConfigurationVariable;
            if (_selectedConfigVariable != null)
            {
                ItrElements.SelectionChanged -= ItrElements_SelectionChanged;
                ItrElements.SelectedItem = null;
                ItrElements.SelectionChanged += ItrElements_SelectionChanged;

                if (ItrInstances != null)
                {
                    ItrInstances.SelectionChanged -= ItrInstances_SelectionChanged;
                    ItrInstances.SelectedItem = null;
                    ItrInstances.SelectionChanged += ItrInstances_SelectionChanged;
                }

                _selectedMacroElement = null;
                GridMacroDetails.Visibility = Visibility.Collapsed;
                if (GridInstanceDetails != null) GridInstanceDetails.Visibility = Visibility.Collapsed;

                GridDetailsEmpty.Visibility = Visibility.Collapsed;
                GridVariableDetails.Visibility = Visibility.Visible;
                GridVariableDetails.DataContext = null;
                GridVariableDetails.DataContext = _selectedConfigVariable;
            }
            else
            {
                GridVariableDetails.Visibility = Visibility.Collapsed;
                if (_selectedMacroElement == null && GridInstanceDetails?.Visibility != Visibility.Visible)
                {
                    GridDetailsEmpty.Visibility = Visibility.Visible;
                }
            }
        }

        private void ItrInstances_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ItrInstances == null) return;
            var selectedInstance = ItrInstances.SelectedItem as TypicalInstance;
            if (selectedInstance != null)
            {
                ItrElements.SelectionChanged -= ItrElements_SelectionChanged;
                ItrElements.SelectedItem = null;
                ItrElements.SelectionChanged += ItrElements_SelectionChanged;

                ItrVariables.SelectionChanged -= ItrVariables_SelectionChanged;
                ItrVariables.SelectedItem = null;
                ItrVariables.SelectionChanged += ItrVariables_SelectionChanged;

                _selectedMacroElement = null;
                _selectedConfigVariable = null;
                GridMacroDetails.Visibility = Visibility.Collapsed;
                GridVariableDetails.Visibility = Visibility.Collapsed;

                GridDetailsEmpty.Visibility = Visibility.Collapsed;
                if (GridInstanceDetails != null)
                {
                    GridInstanceDetails.Visibility = Visibility.Visible;
                    GridInstanceDetails.DataContext = null;
                    GridInstanceDetails.DataContext = selectedInstance;

                    if (string.IsNullOrEmpty(selectedInstance.IsActiveFormula))
                    {
                        RadInstanceActiveAlways.IsChecked = true;
                        if (TxtInstanceActiveFormula != null) TxtInstanceActiveFormula.IsEnabled = false;
                    }
                    else
                    {
                        RadInstanceActiveDependent.IsChecked = true;
                        if (TxtInstanceActiveFormula != null) TxtInstanceActiveFormula.IsEnabled = true;
                    }
                }
            }
            else
            {
                if (GridInstanceDetails != null) GridInstanceDetails.Visibility = Visibility.Collapsed;
                if (_selectedMacroElement == null && _selectedConfigVariable == null)
                {
                    GridDetailsEmpty.Visibility = Visibility.Visible;
                }
            }
        }

        private void RadInstanceActiveAlways_Checked(object sender, RoutedEventArgs e)
        {
            if (ItrInstances == null) return;
            var instance = ItrInstances.SelectedItem as TypicalInstance;
            if (instance == null) return;
            instance.IsActiveFormula = "";
            if (TxtInstanceActiveFormula != null)
            {
                TxtInstanceActiveFormula.Text = "";
                TxtInstanceActiveFormula.IsEnabled = false;
            }
            MarkDirty();
        }

        private void RadInstanceActiveDependent_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtInstanceActiveFormula != null) TxtInstanceActiveFormula.IsEnabled = true;
        }

        private void BtnCollapseLeft_Click(object sender, RoutedEventArgs e)
        {
            ColSelectionArea.Width = new GridLength(0);
        }

        private void BtnExpandLeft_Click(object sender, RoutedEventArgs e)
        {
            ColSelectionArea.Width = new GridLength(240);
        }

        private void BtnCollapseRight_Click(object sender, RoutedEventArgs e)
        {
            ColDetailsArea.Width = new GridLength(0);
        }

        private void BtnExpandRight_Click(object sender, RoutedEventArgs e)
        {
            ColDetailsArea.Width = new GridLength(300);
        }

        private void ColSelectionAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 10)
            {
                if (LeftSplitter != null) LeftSplitter.Visibility = Visibility.Collapsed;
                if (BtnExpandLeft != null) BtnExpandLeft.Visibility = Visibility.Visible;
            }
            else
            {
                if (LeftSplitter != null) LeftSplitter.Visibility = Visibility.Visible;
                if (BtnExpandLeft != null) BtnExpandLeft.Visibility = Visibility.Collapsed;
            }
        }

        private void ColDetailsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 10)
            {
                if (RightSplitter != null) RightSplitter.Visibility = Visibility.Collapsed;
                if (BtnExpandRight != null) BtnExpandRight.Visibility = Visibility.Visible;
            }
            else
            {
                if (RightSplitter != null) RightSplitter.Visibility = Visibility.Visible;
                if (BtnExpandRight != null) BtnExpandRight.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnToggleSearch_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSearch == null) return;
            if (TxtSearch.Visibility == Visibility.Visible)
            {
                TxtSearch.Visibility = Visibility.Collapsed;
                TxtSearch.Text = "";
            }
            else
            {
                TxtSearch.Visibility = Visibility.Visible;
                TxtSearch.Focus();
            }
        }

        private void BtnAddNewFolder_Click(object sender, RoutedEventArgs e)
        {
            ShowToast("å·²åˆ›å»ºæ–°çš„è§„åˆ™åˆ†ç»„æ–‡ä»¶å¤¹");
        }

        private void BtnElementMenu_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            var menu = new ContextMenu();
            
            var previewItem = new MenuItem { Header = "ðŸ‘ Preview" };
            previewItem.Click += (s, ev) => {
                char varChar = (char)('A' + element.VariantIndex);
                string msg = $"æ­£åœ¨æ˜¾ç¤ºå®é¢„è§ˆ:\n\nè·¯å¾„: {element.MacroPath}\nå˜ä½“: Variant {varChar}";
                MessageBox.Show(msg, "æ˜¾ç¤ºé¢„è§ˆ", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            
            var replaceItem = new MenuItem { Header = "â‡… Replace" };
            replaceItem.Click += (s, ev) => ShowMacroNavigator(isAddingNew: false, target: element);
            
            var duplicateItem = new MenuItem { Header = "ðŸ“‘ Duplicate" };
            duplicateItem.Click += (s, ev) => {
                if (_selectedItem is MacroTypical typical)
                {
                    int idx = typical.Elements.IndexOf(element);
                    string json = JsonConvert.SerializeObject(element);
                    var copy = JsonConvert.DeserializeObject<MacroElement>(json);
                    if (idx != -1) typical.Elements.Insert(idx + 1, copy);
                    else typical.Elements.Add(copy);
                    BindElementsList(typical.Elements);
                    MarkDirty();
                    ShowToast("å·²å¤åˆ¶å®");
                }
            };
            
            var deleteItem = new MenuItem { Header = "ðŸ—‘ Delete" };
            deleteItem.Click += (s, ev) => {
                if (_selectedItem is MacroTypical typical)
                {
                    ShowDeleteConfirmation("ç¡®è®¤åˆ é™¤", "æ‚¨ç¡®å®šè¦åˆ é™¤æ­¤å®å…ƒç´ å—ï¼Ÿ", () =>
                    {
                        typical.Elements.Remove(element);
                        BindElementsList(typical.Elements);
                        MarkDirty();
                        ShowToast("å·²åˆ é™¤å®");
                    });
                }
            };
            
            menu.Items.Add(previewItem);
            menu.Items.Add(replaceItem);
            menu.Items.Add(duplicateItem);
            menu.Items.Add(deleteItem);
            
            btn.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void BtnEditSelectionValues_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConfigVariable == null) return;
            
            // Create a simple custom input dialog styled in dark theme
            Window dlg = new Window
            {
                Title = "ç¼–è¾‘é¢„è®¾ä¸‹æ‹‰é€‰é¡¹",
                Width = 350,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };
            
            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            TextBlock lbl = new TextBlock 
            { 
                Text = "è¯·è¾“å…¥ä¸‹æ‹‰é€‰é¡¹(æ¯è¡Œè¾“å…¥ä¸€ä¸ªé€‰é¡¹):", 
                Foreground = Brushes.White, 
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12
            };
            grid.Children.Add(lbl);
            
            TextBox txt = new TextBox 
            { 
                AcceptsReturn = true, 
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Text = string.Join(Environment.NewLine, _selectedConfigVariable.PredefinedValues ?? new List<string>())
            };
            Grid.SetRow(txt, 1);
            grid.Children.Add(txt);
            
            StackPanel btnPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right, 
                Margin = new Thickness(0, 10, 0, 0) 
            };
            Button btnOk = new Button { Content = "ç¡®å®š", Width = 70, Height = 26, Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromRgb(0, 132, 255)), Foreground = Brushes.White };
            Button btnCancel = new Button { Content = "å–æ¶ˆ", Width = 70, Height = 26, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White };
            
            btnOk.Click += (s, ev) => {
                var list = txt.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(line => line.Trim())
                                   .Where(line => !string.IsNullOrEmpty(line))
                                   .ToList();
                _selectedConfigVariable.PredefinedValues = list;
                
                GridVariableDetails.DataContext = null;
                GridVariableDetails.DataContext = _selectedConfigVariable;
                
                dlg.DialogResult = true;
                dlg.Close();
                MarkDirty();
                ShowToast("å·²æ›´æ–°é¢„è®¾ä¸‹æ‹‰é€‰é¡¹");
            };
            
            btnCancel.Click += (s, ev) => dlg.Close();
            
            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);
            
            dlg.Content = grid;
            dlg.ShowDialog();
        }

        private void RadStatusAlways_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            _selectedMacroElement.IsActiveFormula = "";
            if (TxtIsActiveFormula != null)
            {
                TxtIsActiveFormula.Text = "";
                TxtIsActiveFormula.IsEnabled = false;
            }
            MarkDirty();
        }

        private void RadStatusDependent_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            if (TxtIsActiveFormula != null)
            {
                TxtIsActiveFormula.IsEnabled = true;
                TxtIsActiveFormula.Focus();
            }
        }

        private void RadPosMacro_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            _selectedMacroElement.PositionMode = "FromMacro";
            MarkDirty();
        }

        private void RadPosAbsolute_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            _selectedMacroElement.PositionMode = "Absolute";
            MarkDirty();
        }

        private void ChkPosSequential_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            bool isChecked = (sender as CheckBox)?.IsChecked == true;
            if (isChecked)
            {
                _selectedMacroElement.PositionMode = "Sequential";
            }
            else
            {
                _selectedMacroElement.PositionMode = RadPosAbsolute?.IsChecked == true ? "Absolute" : "FromMacro";
            }
            MarkDirty();
        }

        public void ShowToast(string message)
        {
            if (ToastText == null || ToastBorder == null) return;
            ToastText.Text = message;
            ToastBorder.Visibility = Visibility.Visible;

            if (_toastTimer == null)
            {
                _toastTimer = new System.Windows.Threading.DispatcherTimer();
                _toastTimer.Interval = TimeSpan.FromSeconds(3.5);
                _toastTimer.Tick += (s, e) => {
                    ToastBorder.Visibility = Visibility.Collapsed;
                    _toastTimer.Stop();
                };
            }
            else
            {
                _toastTimer.Stop();
            }
            _toastTimer.Start();
        }

        private void BtnCloseToast_Click(object sender, RoutedEventArgs e)
        {
            if (ToastBorder != null) ToastBorder.Visibility = Visibility.Collapsed;
            _toastTimer?.Stop();
        }

        private void UpdateBreadcrumbs()
        {
            if (TxtBreadcrumbs == null) return;
            
            string tabName = "Macro Typicals";
            if (_selectedTab == 0) tabName = "Macro Typicals";
            else if (_selectedTab == 1) tabName = "Typical Groups";
            else if (_selectedTab == 2) tabName = "Configurators";
            else if (_selectedTab == 3) tabName = "Project Builder";
            
            string itemName = "";
            if (_selectedItem != null)
            {
                if (_selectedItem is MacroTypical t) itemName = t.Name;
                else if (_selectedItem is TypicalGroup g) itemName = g.Name;
                else if (_selectedItem is Configurator c) itemName = c.Name;
            }
            
            if (string.IsNullOrEmpty(itemName))
            {
                TxtBreadcrumbs.Text = $"Designer > eBuild_Macro_GettingStarted > {tabName}";
            }
            else
            {
                TxtBreadcrumbs.Text = $"Designer > eBuild_Macro_GettingStarted > {tabName} > {itemName}";
            }
        }

        // ==================== NEW OVERLAY MODAL HANDLERS & HELPERS ====================
        private void BtnAddScriptTypical_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ShowToast("Script Typicals are not supported yet.");
        }



        private void BtnAddConfigGroup_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent Expander toggle!
            if (!(_selectedItem is Configurator config)) return;

            if (ModalAddElements == null || TxtModalAddTitle == null || TxtModalAddSubtitle == null || LstModalAddElements == null) return;

            TxtModalAddTitle.Text = "Add elements";
            TxtModalAddSubtitle.Text = "Select elements to be added to the configurator.";

            // Populate all available TypicalGroups
            _allModalAddElements.Clear();

            foreach (var tg in _db.TypicalGroups)
            {
                bool alreadyAdded = config.TypicalGroupRefs != null && config.TypicalGroupRefs.Contains(tg.Name);
                if (alreadyAdded) continue; // Exclude already associated ones

                _allModalAddElements.Add(new CheckableAddElement
                {
                    Name = tg.Name,
                    Type = "TypicalGroup",
                    Icon = "ðŸ‘¥",
                    IsChecked = false
                });
            }

            foreach (var mt in _db.MacroTypicals)
            {
                string wrappedName = "TG_" + mt.Name;
                bool alreadyAdded = config.TypicalGroupRefs != null && config.TypicalGroupRefs.Contains(wrappedName);
                if (alreadyAdded) continue;

                _allModalAddElements.Add(new CheckableAddElement
                {
                    Name = mt.Name,
                    Type = "MacroTypical",
                    Icon = "ðŸ§¿",
                    IsChecked = false
                });
            }

            TxtModalAddSearch.Text = "";
            FilterModalAddElements();

            ModalAddElements.Visibility = Visibility.Visible;
            UpdateModalAddSelectedCount();
        }

        private Action _onDeleteConfirmed;

        private void ShowDeleteConfirmation(string title, string message, Action onConfirm)
        {
            if (ModalConfirmDelete == null || TxtModalDeleteTitle == null || TxtModalDeleteMessage == null) return;
            TxtModalDeleteTitle.Text = title;
            TxtModalDeleteMessage.Text = message;
            _onDeleteConfirmed = onConfirm;
            ModalConfirmDelete.Visibility = Visibility.Visible;
        }

        private void BtnCloseDeleteModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalConfirmDelete != null)
            {
                ModalConfirmDelete.Visibility = Visibility.Collapsed;
            }
            _onDeleteConfirmed = null;
        }

        private void BtnModalDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (ModalConfirmDelete != null)
            {
                ModalConfirmDelete.Visibility = Visibility.Collapsed;
            }
            var callback = _onDeleteConfirmed;
            _onDeleteConfirmed = null;
            callback?.Invoke();
        }

        private void BtnCloseCreateModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalCreateItem != null)
            {
                ModalCreateItem.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtModalCreateName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BtnModalCreateConfirm == null || TxtModalCreateName == null) return;

            string text = TxtModalCreateName.Text.Trim();
            bool isValid = !string.IsNullOrEmpty(text);

            if (_selectedTab == 0 && text == "MT_") isValid = false;
            else if (_selectedTab == 1 && text == "TG_") isValid = false;

            BtnModalCreateConfirm.IsEnabled = isValid;
        }

        private void BtnModalCreateConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (TxtModalCreateName == null) return;
            string newName = TxtModalCreateName.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            bool isUnique = true;
            if (_selectedTab == 0)
            {
                isUnique = !_db.MacroTypicals.Any(t => string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
            else if (_selectedTab == 1)
            {
                isUnique = !_db.TypicalGroups.Any(g => string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
            else if (_selectedTab == 2)
            {
                isUnique = !_db.Configurators.Any(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            }

            if (!isUnique)
            {
                MessageBox.Show($"Name '{newName}' already exists. Please choose a different name.", "Naming Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            object createdItem = null;
            string itemTypeName = "";

            if (_selectedTab == 0)
            {
                var mt = new MacroTypical { Name = newName };
                _db.MacroTypicals.Add(mt);
                createdItem = mt;
                itemTypeName = "Macro Typical";
            }
            else if (_selectedTab == 1)
            {
                var tg = new TypicalGroup { Name = newName };
                _db.TypicalGroups.Add(tg);
                createdItem = tg;
                itemTypeName = "Typical Group";
            }
            else if (_selectedTab == 2)
            {
                var cfg = new Configurator { Name = newName };
                _db.Configurators.Add(cfg);
                createdItem = cfg;
                itemTypeName = "configurator";
            }

            if (createdItem != null)
            {
                _selectedItem = createdItem;
                RefreshSidebar();
                LstSidebarItems.SelectedItem = _selectedItem;
                UpdateWorkspaceSelection();
                MarkDirty();

                ShowToast($"The {itemTypeName} '{newName}' was created");
            }

            ModalCreateItem.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseAddModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalAddElements != null)
            {
                ModalAddElements.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtModalAddSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterModalAddElements();
        }

        private void CboModalAddFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterModalAddElements();
        }

        private void ChkModalElement_Checked(object sender, RoutedEventArgs e)
        {
            UpdateModalAddSelectedCount();
        }

        private void ChkModalElement_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateModalAddSelectedCount();
        }

        private void FilterModalAddElements()
        {
            if (LstModalAddElements == null) return;

            string search = TxtModalAddSearch?.Text?.Trim()?.ToLower() ?? "";
            int filterIndex = CboModalAddFilter?.SelectedIndex ?? 0;

            var filtered = _allModalAddElements.Where(item => {
                if (filterIndex == 1 && item.Type != "MacroTypical") return false;
                if (filterIndex == 2 && item.Type != "TypicalGroup") return false;

                if (string.IsNullOrEmpty(search)) return true;
                return item.Name.ToLower().Contains(search);
            }).ToList();

            LstModalAddElements.ItemsSource = null;
            LstModalAddElements.ItemsSource = filtered;
        }

        private void UpdateModalAddSelectedCount()
        {
            if (TxtModalAddSelectedCount == null || BtnModalAddConfirm == null) return;

            int checkedCount = _allModalAddElements.Count(item => item.IsChecked);
            TxtModalAddSelectedCount.Text = $"Elements selected: {checkedCount}";
            BtnModalAddConfirm.IsEnabled = checkedCount > 0;
        }

        private void BtnModalAddConfirm_Click(object sender, RoutedEventArgs e)
        {
            var checkedItems = _allModalAddElements.Where(item => item.IsChecked).ToList();
            if (checkedItems.Count == 0) return;

            if (_selectedItem is TypicalGroup group)
            {
                int addedCount = 0;
                foreach (var item in checkedItems)
                {
                    var instance = new TypicalInstance
                    {
                        TargetName = item.Name,
                        Type = item.Type,
                        IsActiveFormula = ""
                    };

                    List<ConfigurationVariable> childVars = null;
                    if (item.Type.Equals("MacroTypical", StringComparison.OrdinalIgnoreCase))
                    {
                        var child = _db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                        childVars = child?.Variables;
                    }
                    else if (item.Type.Equals("TypicalGroup", StringComparison.OrdinalIgnoreCase))
                    {
                        var child = _db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                        childVars = child?.Variables;
                    }

                    if (childVars != null)
                    {
                        foreach (var v in childVars)
                        {
                            instance.VariableMappings.Add(new MappingEntry(v.Name, ""));
                        }
                    }

                    group.Instances.Add(instance);
                    addedCount++;
                }

                BindInstancesList(group.Instances);
                MarkDirty();
                ShowToast($"{addedCount} elements were added.");
            }
            else if (_selectedItem is Configurator config)
            {
                int addedCount = 0;
                bool showWarning = false;
                List<string> warnings = new List<string>();

                foreach (var item in checkedItems)
                {
                    if (item.Type == "TypicalGroup")
                    {
                        if (config.TypicalGroupRefs == null) config.TypicalGroupRefs = new List<string>();
                        if (!config.TypicalGroupRefs.Contains(item.Name))
                        {
                            config.TypicalGroupRefs.Add(item.Name);
                            addedCount++;
                        }
                    }
                    else if (item.Type == "MacroTypical")
                    {
                        string wrappedGroupName = "TG_" + item.Name;
                        var tg = _db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, wrappedGroupName, StringComparison.OrdinalIgnoreCase));
                        if (tg == null)
                        {
                            tg = new TypicalGroup { Name = wrappedGroupName };
                            var mt = _db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                            if (mt != null)
                            {
                                foreach (var v in mt.Variables)
                                {
                                    tg.Variables.Add(new ConfigurationVariable
                                    {
                                        Name = v.Name,
                                        DisplayName = v.DisplayName,
                                        Type = v.Type,
                                        IsMandatory = v.IsMandatory,
                                        VisibilityFormula = v.VisibilityFormula,
                                        PredefinedValues = new List<string>(v.PredefinedValues)
                                    });
                                }
                            }

                            var inst = new TypicalInstance
                            {
                                TargetName = item.Name,
                                Type = "MacroTypical",
                                IsActiveFormula = ""
                            };
                            if (mt != null)
                            {
                                foreach (var v in mt.Variables)
                                {
                                    inst.VariableMappings.Add(new MappingEntry(v.Name, "=" + v.Name));
                                }
                            }
                            tg.Instances.Add(inst);
                            _db.TypicalGroups.Add(tg);
                        }

                        if (config.TypicalGroupRefs == null) config.TypicalGroupRefs = new List<string>();
                        if (!config.TypicalGroupRefs.Contains(tg.Name))
                        {
                            config.TypicalGroupRefs.Add(tg.Name);
                            addedCount++;
                            showWarning = true;
                            warnings.Add($"Macro Typical '{item.Name}' cannot be added directly to a configurator. It has been wrapped in a new Typical Group '{tg.Name}'.");
                        }
                    }
                }

                BindConfigGroupsList(config);
                MarkDirty();

                if (showWarning)
                {
                    MessageBox.Show(string.Join("\n\n", warnings), "Wrapper Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                ShowToast($"{addedCount} elements were added.");
            }

            ModalAddElements.Visibility = Visibility.Collapsed;
        }
    }

    public class CheckableAddElement : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set 
            { 
                if (_isChecked != value)
                {
                    _isChecked = value; 
                    OnPropertyChanged(nameof(IsChecked)); 
                }
            }
        }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Icon { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public class TypicalTargetInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class CheckableTypicalGroup
    {
        public string Name { get; set; }
        public bool IsChecked { get; set; }
    }

    public class StringListConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is List<string> list && list != null)
            {
                return string.Join(", ", list);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string str)
            {
                return str.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(s => !string.IsNullOrEmpty(s))
                          .ToList();
            }
            return new List<string>();
        }
    }

    public class MacroElementHeaderConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "";

            var element = values[1] as MacroElement;
            if (element == null) return "";

            int index = 0;
            if (values[0] is ConfiguratorWindow window)
            {
                if (window._selectedItem is MacroTypical typical)
                {
                    index = typical.Elements.IndexOf(element);
                }
            }
            if (index < 0) index = 0;

            string formattedPath = "";
            if (!string.IsNullOrEmpty(element.MacroPath))
            {
                string clean = element.MacroPath;
                if (clean.StartsWith("$("))
                {
                    int idx = clean.IndexOf(')');
                    if (idx != -1 && idx < clean.Length - 1)
                    {
                        clean = clean.Substring(idx + 1);
                    }
                }
                clean = clean.TrimStart('\\', '/');
                var parts = clean.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                formattedPath = string.Join(" Â» ", parts);
            }
            else
            {
                formattedPath = "æœªæŒ‡å®šå®æ–‡ä»¶";
            }

            char varChar = (char)('A' + element.VariantIndex);
            return $"{index}: EPLAN_Macro Â» {formattedPath} Â» Variant '{varChar}'";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EmpPathToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                string trim = path.Trim();
                if (trim.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) || trim.EndsWith(".ems", StringComparison.OrdinalIgnoreCase))
                {
                    return System.Windows.Visibility.Collapsed;
                }
            }
            return System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MacroPathToMarginConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                string trim = path.Trim();
                if (trim.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) || trim.EndsWith(".ems", StringComparison.OrdinalIgnoreCase))
                {
                    return new System.Windows.Thickness(20, 0, 0, 12);
                }
            }
            return new System.Windows.Thickness(0, 0, 0, 12);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MacroPathToFileNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    return System.IO.Path.GetFileName(path);
                }
                catch
                {
                    return path;
                }
            }
            return "æœªæŒ‡å®šå®æ–‡ä»¶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}