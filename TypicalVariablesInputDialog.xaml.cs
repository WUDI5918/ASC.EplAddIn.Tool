using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class TypicalVariablesInputDialog : Window
    {
        private List<ConfigurationVariable> _variables;
        private Dictionary<string, FrameworkElement> _controlMap = new Dictionary<string, FrameworkElement>();
        private Dictionary<string, FrameworkElement> _rowMap = new Dictionary<string, FrameworkElement>();

        public Dictionary<string, string> VariableValues { get; private set; } = new Dictionary<string, string>();

        public TypicalVariablesInputDialog(List<ConfigurationVariable> variables, string typicalName)
        {
            InitializeComponent();
            _variables = variables;
            TxtTitle.Text = $"宏规则配置 - {typicalName}";

            BuildForm();
        }

        private void BuildForm()
        {
            VariablesFormPanel.Children.Clear();

            // Group variables by category
            var categorizedVars = new Dictionary<string, List<ConfigurationVariable>>();
            foreach (var variable in _variables)
            {
                string cat = string.IsNullOrWhiteSpace(variable.Category) ? "常规参数" : variable.Category.Trim();
                if (!categorizedVars.ContainsKey(cat))
                {
                    categorizedVars[cat] = new List<ConfigurationVariable>();
                }
                categorizedVars[cat].Add(variable);
            }

            foreach (var categoryKvp in categorizedVars)
            {
                // Add category separator/header
                var headerText = new TextBlock
                {
                    Text = categoryKvp.Key,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)), // Light slate text
                    Margin = new Thickness(0, 10, 0, 6)
                };
                VariablesFormPanel.Children.Add(headerText);

                var catBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 45)),   // Dark border
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),    // Dark card background
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var catPanel = new StackPanel();

                foreach (var variable in categoryKvp.Value)
                {
                    var rowGrid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    string dispName = string.IsNullOrEmpty(variable.DisplayName) ? variable.Name : variable.DisplayName;
                    if (variable.IsMandatory) dispName += " *";

                    var label = new Label
                    {
                        Content = dispName,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
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
                        cbo.SelectionChanged += (s, ev) => EvaluateVisibility();
                        inputCtrl = cbo;
                    }
                    else if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                    {
                        var chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                        bool.TryParse(defaultVal, out bool defBool);
                        chk.IsChecked = defBool;
                        chk.Checked += (s, ev) => EvaluateVisibility();
                        chk.Unchecked += (s, ev) => EvaluateVisibility();
                        inputCtrl = chk;
                    }
                    else
                    {
                        var txt = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
                        txt.Text = defaultVal;
                        txt.TextChanged += (s, ev) => EvaluateVisibility();
                        inputCtrl = txt;
                    }

                    Grid.SetColumn(inputCtrl, 1);
                    rowGrid.Children.Add(inputCtrl);

                    _controlMap[variable.Name] = inputCtrl;
                    _rowMap[variable.Name] = rowGrid;
                    catPanel.Children.Add(rowGrid);
                }

                catBorder.Child = catPanel;
                VariablesFormPanel.Children.Add(catBorder);
            }

            // Apply visibility formulas after the whole form is built
            EvaluateVisibility();
        }

        /// <summary>
        /// Reads the current control values as a typed dictionary, mirroring how the
        /// generation engine types parameters so visibility formulas evaluate correctly.
        /// </summary>
        private Dictionary<string, object> BuildCurrentValues()
        {
            var values = new Dictionary<string, object>();
            foreach (var variable in _variables)
            {
                if (!_controlMap.TryGetValue(variable.Name, out var ctrl))
                {
                    values[variable.Name] = null;
                    continue;
                }

                if (ctrl is ComboBox cbo)
                {
                    string s = cbo.SelectedItem?.ToString() ?? "";
                    if (variable.Type.Equals("Integer", StringComparison.OrdinalIgnoreCase) && int.TryParse(s, out int i))
                        values[variable.Name] = i;
                    else if (variable.Type.Equals("Double", StringComparison.OrdinalIgnoreCase) && double.TryParse(s, out double d))
                        values[variable.Name] = d;
                    else if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) && bool.TryParse(s, out bool b))
                        values[variable.Name] = b;
                    else
                        values[variable.Name] = s;
                }
                else if (ctrl is CheckBox chk)
                {
                    values[variable.Name] = chk.IsChecked == true;
                }
                else if (ctrl is TextBox txt)
                {
                    string s = txt.Text?.Trim() ?? "";
                    if (variable.Type.Equals("Integer", StringComparison.OrdinalIgnoreCase) && int.TryParse(s, out int i))
                        values[variable.Name] = i;
                    else if (variable.Type.Equals("Double", StringComparison.OrdinalIgnoreCase) && double.TryParse(s, out double d))
                        values[variable.Name] = d;
                    else if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) && bool.TryParse(s, out bool b))
                        values[variable.Name] = b;
                    else
                        values[variable.Name] = s;
                }
                else
                {
                    values[variable.Name] = null;
                }
            }
            return values;
        }

        /// <summary>
        /// Shows or hides each variable row based on its VisibilityFormula, so hidden
        /// mandatory variables do not block the user.
        /// </summary>
        private void EvaluateVisibility()
        {
            var values = BuildCurrentValues();
            foreach (var variable in _variables)
            {
                if (!_rowMap.TryGetValue(variable.Name, out var row)) continue;

                if (string.IsNullOrWhiteSpace(variable.VisibilityFormula))
                {
                    row.Visibility = Visibility.Visible;
                }
                else
                {
                    row.Visibility = ConfiguratorService.EvaluateActiveFormula(variable.VisibilityFormula, values)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // Validate mandatory values
            foreach (var variable in _variables)
            {
                if (_controlMap.TryGetValue(variable.Name, out var ctrl))
                {
                    string strValue = "";
                    if (ctrl is ComboBox cbo)
                    {
                        strValue = cbo.SelectedItem?.ToString() ?? "";
                    }
                    else if (ctrl is CheckBox chk)
                    {
                        strValue = (chk.IsChecked == true).ToString();
                    }
                    else if (ctrl is TextBox txt)
                    {
                        strValue = txt.Text?.Trim() ?? "";
                    }

                    bool isVisible = !_rowMap.TryGetValue(variable.Name, out var row) || row.Visibility != Visibility.Collapsed;
                    if (variable.IsMandatory && isVisible && string.IsNullOrEmpty(strValue))
                    {
                        MessageBox.Show($"参数 [{variable.DisplayName ?? variable.Name}] 是必填项，不能为空！", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ctrl.Focus();
                        return;
                    }

                    VariableValues[variable.Name] = strValue;
                }
            }

            this.DialogResult = true;
            this.Close();
        }
    }
}
