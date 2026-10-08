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
        // ==================== VARIABLES LOGIC ====================
        private void BindVariablesList(List<ConfigurationVariable> list)
        {
            ItrVariables.ItemsSource = null;
            ItrVariables.ItemsSource = list;
            if (ExpVariables != null)
            {
                ExpVariables.Header = $"Configuration variables ({(list?.Count ?? 0)})";
            }
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
                var newVar = new ConfigurationVariable { Name = "New_Variable_" + (vars.Count + 1), DisplayName = "新变量" };
                vars.Add(newVar);
                BindVariablesList(vars);
                MarkDirty();
                ShowToast($"已成功添加变量 '{newVar.Name}'");
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
                ShowDeleteConfirmation("确认删除", $"确定删除变量 '{varItem.Name}' 吗？", () =>
                {
                    vars.Remove(varItem);
                    BindVariablesList(vars);
                    MarkDirty();
                    ShowToast($"已删除变量 '{varItem.Name}'");
                });
            }
        }

        // ==================== ELEMENTS / MACROS LOGIC ====================
        private void BindElementsList(List<MacroElement> list)
        {
            ItrElements.ItemsSource = null;
            ItrElements.ItemsSource = list;
            if (ExpElements != null)
            {
                ExpElements.Header = $"Macros ({(list?.Count ?? 0)})";
            }
            if (GridElementsEmptyState != null)
            {
                GridElementsEmptyState.Visibility = (list == null || list.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnAddElement_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var selected = TvMacros.SelectedItem as MacroTreeNode;
            ApplyMacroSelection(selected, forceAddNew: true);
        }

        private void BtnDeleteElement_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var elementItem = btn.DataContext as MacroElement;
            if (elementItem == null) return;

            if (_selectedItem is MacroTypical typical)
            {
                ShowDeleteConfirmation("确认删除", "您确定要删除此宏元素吗？", () =>
                {
                    typical.Elements.Remove(elementItem);
                    BindElementsList(typical.Elements);
                    MarkDirty();
                    ShowToast("已成功删除宏文件");
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

            string resolvedPath = ConfiguratorService.ResolveMacroPath(element.MacroPath);
            if (string.IsNullOrEmpty(element.MacroPath) || !System.IO.File.Exists(resolvedPath))
            {
                MessageBox.Show($"未找到宏文件，无法预览: {element.MacroPath}", "预览失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var ctx = new Eplan.EplApi.ApplicationFramework.ActionCallingContext();
                ctx.AddParameter("filename", resolvedPath);
                ctx.AddParameter("variant", element.VariantIndex.ToString());
                new Eplan.EplApi.ApplicationFramework.CommandLineInterpreter().Execute("XMacMInsertMacro", ctx);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法调用 EPLAN 预览宏: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnShowElementRefs_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            if (string.IsNullOrEmpty(element.MacroPath))
            {
                MessageBox.Show("此元素尚未指定宏文件路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var refs = new List<string>();
            foreach (var typical in _db.MacroTypicals)
            {
                if (typical.Elements.Any(el => string.Equals(el.MacroPath, element.MacroPath, StringComparison.OrdinalIgnoreCase)))
                {
                    refs.Add($"宏规则: {typical.Name}");
                }
            }

            if (refs.Count == 0)
            {
                MessageBox.Show($"宏文件 \"{element.MacroPath}\" 没有被其他规则参考。", "参考关系", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show Rich Modal
            var dlg = new Window
            {
                Title = $"引用参考 - {System.IO.Path.GetFileName(element.MacroPath)}",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground = Brushes.White
            };
            
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var txt = new TextBlock 
            { 
                Text = $"文件 '{element.MacroPath}' 被以下位置参考：", 
                TextWrapping = TextWrapping.Wrap, 
                Margin = new Thickness(0,0,0,10)
            };
            Grid.SetRow(txt, 0);
            grid.Children.Add(txt);

            var lst = new ListBox
            {
                ItemsSource = refs,
                Background = Brushes.Transparent,
                Foreground = Brushes.LightSkyBlue,
                BorderThickness = new Thickness(0)
            };
            Grid.SetRow(lst, 1);
            grid.Children.Add(lst);

            dlg.Content = grid;
            dlg.ShowDialog();
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
                ShowToast("已在下方添加宏元素");
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
                ShowToast("已复制选中的宏元素");
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string clean = path.Replace("/", "\\").Trim('\\');
            if (clean.StartsWith("$("))
            {
                int idx = clean.IndexOf(')');
                if (idx != -1 && idx < clean.Length - 1)
                {
                    clean = clean.Substring(idx + 1).Trim('\\');
                }
            }
            return clean;
        }

        private List<string> GetPlaceholdersForElement(MacroElement element)
        {
            if (element == null || string.IsNullOrWhiteSpace(element.MacroPath)) return new List<string>();

            // 1. Try to find from our in-memory cache first
            if (_allProjectMacros != null && _allProjectMacros.Count > 0)
            {
                var match = _allProjectMacros.FirstOrDefault(m => 
                    string.Equals(NormalizePath(m.MacroPath), NormalizePath(element.MacroPath), StringComparison.OrdinalIgnoreCase) && 
                    m.VariantIndex == element.VariantIndex
                );

                if (match != null && match.Placeholders != null && match.Placeholders.Count > 0)
                {
                    ConfiguratorService.LogDebug($"GetPlaceholdersForElement: Found placeholders in memory cache for '{element.MacroPath}' (Variant {element.VariantIndex}): {string.Join(", ", match.Placeholders)}");
                    return match.Placeholders;
                }
            }

            // 2. Fallback to extracting from macro file on disk / EPLAN API insertion
            ConfiguratorService.LogDebug($"GetPlaceholdersForElement: Cache miss for '{element.MacroPath}'. Falling back to disk/API extraction.");
            return ConfiguratorService.ExtractPlaceholdersFromMacroFile(element.MacroPath, element.VariantIndex);
        }

        private void AutoExtractPlaceholdersForElement(MacroElement element)
        {
            if (element == null || string.IsNullOrWhiteSpace(element.MacroPath)) return;
            try
            {
                var placeholders = GetPlaceholdersForElement(element);
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

        // ==================== TYPICAL INSTANCES LOGIC ====================
        private void BindInstancesList(List<TypicalInstance> list)
        {
            ItrInstances.ItemsSource = null;
            ItrInstances.ItemsSource = list;
            if (ExpInstances != null)
            {
                ExpInstances.Header = $"Macro Typicals and Typical Groups ({(list?.Count ?? 0)})";
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
                    Icon = "🧿", // Concentric circles icon
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
                    Icon = "👥", // Group icon
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
                ShowDeleteConfirmation("确认删除", "您确定要断开与此规则实例的关联吗？", () =>
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

            // Prevent recursive UI refresh loop during initial data binding
            if (string.Equals(instance.TargetName, targetInfo.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(instance.Type, targetInfo.Type, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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

        private List<TypicalTargetInfo> _allTypicalTargetsCached = null;
        private string _cachedForGroup = null;

        public List<TypicalTargetInfo> AllTypicalTargets
        {
            get
            {
                string currentGroup = (_selectedItem is TypicalGroup g) ? g.Name : "";
                if (_allTypicalTargetsCached == null || _cachedForGroup != currentGroup)
                {
                    _cachedForGroup = currentGroup;
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
                        foreach (var tg in _db.TypicalGroups)
                        {
                            if (!string.IsNullOrEmpty(currentGroup) && string.Equals(tg.Name, currentGroup, StringComparison.OrdinalIgnoreCase))
                            {
                                continue; // Prevent self-nesting
                            }
                            list.Add(new TypicalTargetInfo { Name = tg.Name, Type = "TypicalGroup" });
                        }
                    }
                    _allTypicalTargetsCached = list;
                }
                return _allTypicalTargetsCached;
            }
        }

        public void InvalidateTypicalTargetsCache()
        {
            _allTypicalTargetsCached = null;
            _cachedForGroup = null;
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
                if (ExpConfigGroups != null)
                {
                    ExpConfigGroups.Header = $"Associated Rule Groups ({(config.TypicalGroupRefs?.Count ?? 0)})";
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
                ShowDeleteConfirmation("确认删除", $"确定要断开规则组 '{groupName}' 的关联吗？", () =>
                {
                    config.TypicalGroupRefs.Remove(groupName);
                    BindConfigGroupsList(config);
                    MarkDirty();
                    ShowToast($"已断开规则组 '{groupName}' 的关联");
                });
            }
        }

        private void BtnToggleColumns_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var contextMenu = new ContextMenu();

            // Name Column
            var miName = new MenuItem { Header = "Name", IsCheckable = true, IsChecked = ColName.Visibility == Visibility.Visible };
            miName.Click += (s, ev) => ColName.Visibility = miName.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            contextMenu.Items.Add(miName);

            // Path Column
            var miPath = new MenuItem { Header = "Path", IsCheckable = true, IsChecked = ColPath.Visibility == Visibility.Visible };
            miPath.Click += (s, ev) => ColPath.Visibility = miPath.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            contextMenu.Items.Add(miPath);

            // Variant Column
            var miVariant = new MenuItem { Header = "Variant", IsCheckable = true, IsChecked = ColVariant.Visibility == Visibility.Visible };
            miVariant.Click += (s, ev) => ColVariant.Visibility = miVariant.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            contextMenu.Items.Add(miVariant);

            // RepType Column
            var miRepType = new MenuItem { Header = "Representation type", IsCheckable = true, IsChecked = ColRepType.Visibility == Visibility.Visible };
            miRepType.Click += (s, ev) => ColRepType.Visibility = miRepType.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            contextMenu.Items.Add(miRepType);

            button.ContextMenu = contextMenu;
            button.ContextMenu.IsOpen = true;
        }

    }
}
