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
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "宏规则";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.ToolTip = "新增宏规则";
                    BtnAddNewItem.Visibility = Visibility.Visible;
                }
            }
            else if (_selectedTab == 1)
            {
                var list = _db.TypicalGroups.Where(i => string.IsNullOrEmpty(searchTxt) || i.Name.ToLower().Contains(searchTxt)).ToList();
                LstSidebarItems.ItemsSource = list;
                count = list.Count;
                emptyText = "No Typical Groups";
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "规则组";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.ToolTip = "新增规则组";
                    BtnAddNewItem.Visibility = Visibility.Visible;
                }
            }
            else if (_selectedTab == 2)
            {
                var list = _db.Configurators.Where(i => string.IsNullOrEmpty(searchTxt) || i.Name.ToLower().Contains(searchTxt)).ToList();
                LstSidebarItems.ItemsSource = list;
                count = list.Count;
                emptyText = "No configurators";
                if (TxtSidebarHeader != null) TxtSidebarHeader.Text = "配置器";
                if (BtnAddNewItem != null)
                {
                    BtnAddNewItem.ToolTip = "新增配置器";
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

            InvalidateTypicalTargetsCache();
            RefreshSidebar();
        }

        private void BtnCopySidebarItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            object item = btn.DataContext;
            if (item == null || item == this)
            {
                item = LstSidebarItems.SelectedItem;
            }
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
                InvalidateTypicalTargetsCache();
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
            if (item == null || item == this)
            {
                item = LstSidebarItems.SelectedItem;
            }
            if (item == null) return;

            string itemName = "";
            if (item is MacroTypical t) itemName = t.Name;
            else if (item is TypicalGroup g) itemName = g.Name;
            else if (item is Configurator c) itemName = c.Name;

            ShowDeleteConfirmation("确认删除", $"您确定要删除项 '{itemName}' 吗？", () =>
            {
                bool wasSelected = (_selectedItem == item);

                if (item is MacroTypical typical) _db.MacroTypicals.Remove(typical);
                else if (item is TypicalGroup group) _db.TypicalGroups.Remove(group);
                else if (item is Configurator config) _db.Configurators.Remove(config);

                InvalidateTypicalTargetsCache();

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

            ShowDeleteConfirmation("确认删除", "您确定要删除此项目吗？", () =>
            {
                if (_selectedItem is MacroTypical typical) _db.MacroTypicals.Remove(typical);
                else if (_selectedItem is TypicalGroup group) _db.TypicalGroups.Remove(group);
                else if (_selectedItem is Configurator config) _db.Configurators.Remove(config);

                _selectedItem = null;
                InvalidateTypicalTargetsCache();
                RefreshSidebar();
                UpdateWorkspaceSelection();
                MarkDirty();
            });
        }

    }
}
