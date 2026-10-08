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
                    MessageBox.Show("请先选择一个 [配置器] 以便自动绘图！", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtStatus.Text = "正在生成原理图...";
                TxtStatus.Foreground = Brushes.Blue;

                var groupParameters = new Dictionary<string, Dictionary<string, object>>();
                foreach (var state in _builderStates)
                {
                    groupParameters[state.Group.Name] = GetGroupParameters(state);
                }

                // P-3: pre-flight validation before generation.
                var preflight = PreflightValidator.ValidateConfigurator(_db, config, groupParameters);
                if (preflight.HasErrors)
                {
                    TxtStatus.Text = "预检失败，已中止生成。";
                    TxtStatus.Foreground = Brushes.Red;
                    MessageBox.Show(preflight.ToString(), "生成前预检失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (preflight.HasWarnings)
                {
                    int warningCount = preflight.Issues.Count(i => i.Severity == PreflightSeverity.Warning);
                    TxtStatus.Text = $"预检通过（有 {warningCount} 个警告，继续生成）";
                    TxtStatus.Foreground = Brushes.Orange;
                }

                string templatePath = TxtTargetTemplate.Text.Trim();
                string targetPath = TxtTargetProject.Text.Trim();

                Action generateAction = () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(templatePath) && !string.IsNullOrEmpty(targetPath))
                        {
                            this.Dispatcher.Invoke(() =>
                            {
                                TxtStatus.Text = "正在创建并激活目标项目...";
                            });
                            ConfiguratorService.CreateAndActivateProject(templatePath, targetPath);
                        }

                        string result = ConfiguratorService.GenerateConfigurator(config, _db, groupParameters);

                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = result;
                            TxtStatus.Foreground = (result.StartsWith("错误") || result.StartsWith("发生异常")) ? Brushes.Red : Brushes.Green;
                        });
                    }
                    catch (Exception ex)
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = $"错误：生成失败！{ex.Message}";
                            TxtStatus.Foreground = Brushes.Red;
                        });
                        throw;
                    }
                };

                var logWindow = new GenerationLogWindow(this, generateAction);
                logWindow.ShowDialog();
            }
            else
            {
                if (!(_selectedItem is MacroTypical typical))
                {
                    MessageBox.Show("请先选择一个 [宏规则] 以便自动绘图！", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TxtStatus.Text = "正在解析规则并批量生成图纸...";
                TxtStatus.Foreground = Brushes.Blue;

                Action generateAction = () =>
                {
                    try
                    {
                        // Pass the in-memory _db so unsaved edits in the UI take effect (t10);
                        // GenerateTypical falls back to the on-disk library when db is null.
                        string result = ConfiguratorService.GenerateTypical(typical, _db);

                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = result;
                            TxtStatus.Foreground = (result.StartsWith("错误") || result.StartsWith("发生异常")) ? Brushes.Red : Brushes.Green;
                        });
                    }
                    catch (Exception ex)
                    {
                        this.Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = $"错误：生成失败！{ex.Message}";
                            TxtStatus.Foreground = Brushes.Red;
                        });
                        throw;
                    }
                };

                var logWindow = new GenerationLogWindow(this, generateAction);
                logWindow.ShowDialog();
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

        private void BtnSelectTargetTemplate_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "EPLAN Project Templates (*.ept;*.zw1)|*.ept;*.zw1|All files (*.*)|*.*",
                Title = "选择基础项目模板"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                TxtTargetTemplate.Text = openFileDialog.FileName;
            }
        }

        private void BtnSelectTargetProject_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "EPLAN Project (*.elk)|*.elk",
                Title = "选择新项目保存路径",
                FileName = "NewProject.elk"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                TxtTargetProject.Text = saveFileDialog.FileName;
            }
        }

        private void RenderProjectBuilderForm(Configurator runConfig)
        {
            PanelBuilderForm.Children.Clear();
            _builderStates.Clear();

            if (runConfig.TypicalGroupRefs == null || runConfig.TypicalGroupRefs.Count == 0)
            {
                var emptyMsg = new TextBlock
                {
                    Text = "此配置器尚未关联任何规则组！请在设计模式下关联规则组后再进行生成。",
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
                    Text = $"规则组: {group.Name}",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 15, 0, 10)
                };
                PanelBuilderForm.Children.Add(header);

                var groupPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 15) };

                if (group.Variables == null || group.Variables.Count == 0)
                {
                    var noVars = new TextBlock
                    {
                        Text = "该规则组无配置变量",
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
                        .GroupBy(v => string.IsNullOrWhiteSpace(v.Category) ? "常规参数" : v.Category.Trim())
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

                                txt.TextChanged += (s, ev) =>
                                {
                                    if (_builderDebounceTimer == null)
                                    {
                                        _builderDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                                        _builderDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
                                        _builderDebounceTimer.Tick += (senderTick, eTick) =>
                                        {
                                            _builderDebounceTimer.Stop();
                                            EvaluateVisibilityForGroup(state);
                                        };
                                    }
                                    _builderDebounceTimer.Stop();
                                    _builderDebounceTimer.Start();
                                };
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
                int row = Grid.GetRow(btn);
                TextBox targetTextBox = null;
                foreach (UIElement child in parentGrid.Children)
                {
                    if (child is TextBox txt && Grid.GetRow(txt) == row)
                    {
                        targetTextBox = txt;
                        break;
                    }
                }

                if (targetTextBox == null) return;

                List<string> varNames = new List<string>();
                if (_selectedItem is MacroTypical typical)
                {
                    varNames.AddRange(typical.Variables.Select(v => v.Name));
                }
                else if (_selectedItem is TypicalGroup group)
                {
                    varNames.AddRange(group.Variables.Select(v => v.Name));
                }

                var dialog = new FormulaEditorDialog(targetTextBox.Text, varNames)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    targetTextBox.Text = dialog.FormulaText;
                    targetTextBox.Focus();
                    targetTextBox.SelectionStart = targetTextBox.Text.Length;
                }
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
                    txt.ToolTip = "公式有效";
                }
            }
        }

        private void BtnExtractPlaceholders_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MacroElement element)
            {
                if (string.IsNullOrWhiteSpace(element.MacroPath))
                {
                    MessageBox.Show("请先指定宏文件路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var placeholders = GetPlaceholdersForElement(element);
                    if (placeholders == null || placeholders.Count == 0)
                    {
                        MessageBox.Show("未提取到任何占位符变量。请确保宏文件存在且包含占位符，且当前在EPLAN中已打开了一个项目。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

                    MessageBox.Show($"成功提取 {placeholders.Count} 个占位符变量！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"提取占位符失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "导入规则库配置"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var importedData = JsonConvert.DeserializeObject<ConfiguratorData>(json);
                    if (importedData == null)
                    {
                        MessageBox.Show("导入的文件格式不正确或为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var result = MessageBox.Show(
                        "是否覆盖当前库的配置？\n选择 [是] 将完全覆盖现有配置。\n选择 [否] 将合并新配置（根据名称避免重复）。",
                        "导入选项",
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

                    MessageBox.Show("规则库导入成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportLibrary_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "configurator_library.json",
                Title = "导出规则库配置"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(_db, Formatting.Indented);
                    File.WriteAllText(saveFileDialog.FileName, json);
                    MessageBox.Show("规则库导出成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportLibraryXlsx_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                FileName = "configurator_library.xlsx",
                Title = "导出规则库配置 (XLSX)"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    ConfiguratorService.ExportFullConfigToXlsx(_db, saveFileDialog.FileName);
                    MessageBox.Show("规则库 XLSX 导出成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportLibraryXlsx_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                Title = "导入规则库配置 (XLSX)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var importedData = ConfiguratorService.ImportFullConfigFromXlsx(openFileDialog.FileName);
                    if (importedData == null)
                    {
                        MessageBox.Show("导入的文件格式不正确或为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var result = MessageBox.Show(
                        "是否覆盖当前库的配置？\n选择 [是] 将完全覆盖现有配置。\n选择 [否] 将合并新配置（根据名称避免重复）。",
                        "导入选项",
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

                    MessageBox.Show("规则库 XLSX 导入成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLibrarySettings_Click(object sender, RoutedEventArgs e)
        {
            if (_db.Settings == null)
            {
                _db.Settings = new LibrarySettings();
            }

            var dialog = new LibrarySettingsDialog(_db.Settings, _db, restored =>
            {
                _db = restored ?? new ConfiguratorData();
                if (_db.Settings == null) _db.Settings = new LibrarySettings();
                InvalidateTypicalTargetsCache();
                RefreshSidebar();
                UpdateWorkspaceSelection();
                MarkDirty();
            });
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                _db.Settings = dialog.Settings;
                MarkDirty();
                MessageBox.Show("库属性设置已保存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnImportBuilderConfig_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                Title = "导入 Project Builder 变量配置"
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
                    MessageBox.Show("未能在导入的文件中找到任何有效的变量配置！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                MessageBox.Show($"成功导入并填充了 {filledCount} 个配置变量的值！", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExportBuilderConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!(_selectedItem is Configurator config)) return;

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "XML files (*.xml)|*.xml|Excel files (*.xlsx)|*.xlsx",
                FileName = $"{config.Name}_Config",
                Title = "导出 Project Builder 变量配置"
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

                MessageBox.Show("配置变量导出成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show($"解析 XML 文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"导出 XML 文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"解析 Excel 文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

                        // Group header row: col1 = group name, col2 stays empty, variable
                        // names start at col3. This matches the import parser, which only
                        // begins group-header detection when column 2 is empty.
                        sheet.Cells[row, 1].Value = groupName;
                        sheet.Cells[row, 1].Style.Font.Bold = true;

                        var groupVars = values.Keys.Where(k => k.StartsWith(groupName + ".")).Select(k => k.Substring(groupName.Length + 1)).ToList();
                        for (int i = 0; i < groupVars.Count; i++)
                        {
                            sheet.Cells[row, i + 3].Value = groupVars[i];
                            sheet.Cells[row, i + 3].Style.Font.Bold = true;
                        }
                        row++;

                        // Group data row: col1 = group name, col2 empty, values start at col3.
                        sheet.Cells[row, 1].Value = groupName;
                        for (int i = 0; i < groupVars.Count; i++)
                        {
                            string key = $"{groupName}.{groupVars[i]}";
                            sheet.Cells[row, i + 3].Value = values[key];
                        }
                        row++;
                    }

                    sheet.Cells.AutoFitColumns();
                    package.SaveAs(new FileInfo(filePath));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出 Excel 文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                                        firstError = $"必填参数 [{variable.DisplayName ?? variable.Name}] 不能为空！";
                                    }
                                    if (ctrl is Control c) c.BorderBrush = Brushes.Red;
                                }
                                else
                                {
                                    if (ctrl is Control c) c.ClearValue(Control.BorderBrushProperty);
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
                TxtStatus.Text = "就绪";
                TxtStatus.Foreground = Brushes.Green;
            }
        }

    }
}
