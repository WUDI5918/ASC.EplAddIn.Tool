using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    /// <summary>
    /// Code-only editor dialog for creating/editing a <see cref="ScriptTypical"/>.
    /// </summary>
    public class ScriptTypicalEditorDialog : Window
    {
        public ScriptTypical Result { get; private set; }

        private readonly TextBox _txtName = new TextBox();
        private readonly ComboBox _cboTrigger = new ComboBox();
        private readonly TextBox _txtOrder = new TextBox();
        private readonly TextBox _txtActiveFormula = new TextBox();
        private readonly TextBox _txtContent = new TextBox();

        public ScriptTypicalEditorDialog(ScriptTypical script)
        {
            Title = script == null ? "新增脚本规则 (Script Typical)" : "编辑脚本规则 (Script Typical)";
            Width = 620;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
            ResizeMode = ResizeMode.CanResize;

            var source = script ?? new ScriptTypical();
            _txtName.Text = source.Name ?? "";
            _cboTrigger.Items.Add("BeforeGenerate");
            _cboTrigger.Items.Add("AfterGenerate");
            _cboTrigger.SelectedItem = string.IsNullOrWhiteSpace(source.TriggerPoint) ? "AfterGenerate" : source.TriggerPoint;
            _txtOrder.Text = source.Order.ToString();
            _txtActiveFormula.Text = string.IsNullOrWhiteSpace(source.IsActiveFormula) ? "true" : source.IsActiveFormula;
            _txtContent.Text = source.ScriptContent ?? "";

            _txtContent.AcceptsReturn = true;
            _txtContent.TextWrapping = TextWrapping.NoWrap;
            _txtContent.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtContent.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _txtContent.FontFamily = new FontFamily("Consolas, Courier New");
            _txtContent.FontSize = 12;

            Content = BuildLayout();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var form = new StackPanel();

            form.Children.Add(BuildLabeledRow("名称 (Name)", _txtName));
            form.Children.Add(BuildLabeledRow("触发点 (Trigger Point)", _cboTrigger));
            form.Children.Add(BuildLabeledRow("执行顺序 (Order)", _txtOrder));
            form.Children.Add(BuildLabeledRow("激活条件公式 (IsActiveFormula)", _txtActiveFormula));

            var contentLabel = new TextBlock
            {
                Text = "脚本内容 (Script Content — JavaScript)",
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Margin = new Thickness(0, 10, 0, 4)
            };
            form.Children.Add(contentLabel);
            form.Children.Add(_txtContent);

            Grid.SetRow(form, 0);
            root.Children.Add(form);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            var btnCancel = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 30,
                IsCancel = true,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))
            };
            var btnOk = new Button
            {
                Content = "确定",
                Width = 80,
                Height = 30,
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xFF)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOk.Click += BtnOk_Click;
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnOk);

            Grid.SetRow(btnPanel, 1);
            root.Children.Add(btnPanel);

            return root;
        }

        private static UIElement BuildLabeledRow(string label, Control control)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0))
            };
            Grid.SetColumn(lbl, 0);

            control.Height = 28;
            control.VerticalContentAlignment = VerticalAlignment.Center;
            control.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            control.Foreground = Brushes.White;
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            Grid.SetColumn(control, 1);

            grid.Children.Add(lbl);
            grid.Children.Add(control);
            return grid;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string name = _txtName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("脚本名称不能为空。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(_txtOrder.Text?.Trim(), out int order))
            {
                order = 0;
            }

            Result = new ScriptTypical
            {
                Name = name,
                TriggerPoint = _cboTrigger.SelectedItem?.ToString() ?? "AfterGenerate",
                Order = order,
                IsActiveFormula = string.IsNullOrWhiteSpace(_txtActiveFormula.Text) ? "true" : _txtActiveFormula.Text.Trim(),
                ScriptContent = _txtContent.Text ?? ""
            };

            DialogResult = true;
            Close();
        }
    }
}
