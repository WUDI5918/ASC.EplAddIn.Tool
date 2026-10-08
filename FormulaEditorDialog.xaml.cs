using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class FormulaEditorDialog : Window
    {
        public string FormulaText { get; private set; }

        // --- Autocomplete ---
        private CompletionWindow _completionWindow;
        private List<ICompletionData> _allCompletionItems;

        // --- Real-time validation ---
        private DispatcherTimer _validationTimer;
        private static readonly SolidColorBrush BorderDefault;
        private static readonly SolidColorBrush BorderValid;
        private static readonly SolidColorBrush BorderInvalid;

        // --- Variables panel ---
        private readonly List<string> _availableVariables;
        private ListCollectionView _allVariables;

        static FormulaEditorDialog()
        {
            BorderDefault = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            BorderValid = new SolidColorBrush(Color.FromRgb(0x2D, 0x7D, 0x46));
            BorderInvalid = new SolidColorBrush(Color.FromRgb(0xE8, 0x40, 0x40));
            BorderDefault.Freeze();
            BorderValid.Freeze();
            BorderInvalid.Freeze();
        }

        public FormulaEditorDialog(string initialText, List<string> availableVariables)
        {
            InitializeComponent();
            txtEditor.Text = initialText ?? "";

            _availableVariables = availableVariables ?? new List<string>();

            // NCalc-specific syntax highlighting (variables, numbers, strings, functions, operators)
            txtEditor.SyntaxHighlighting = BuildNCalcHighlighting(_availableVariables);

            // Build the operator / function toolbar
            BuildToolbar();

            // Populate variables with grouping (project variables vs. system variables)
            PopulateVariables(_availableVariables);

            // Build completion data list
            _allCompletionItems = new List<ICompletionData>();

            // Add variables
            foreach (var v in _availableVariables)
            {
                _allCompletionItems.Add(new VariableCompletionData(v, "变量"));
            }

            // Add system variables
            foreach (var sysVar in new[] { "ProjectName", "Date", "User" })
            {
                _allCompletionItems.Add(new VariableCompletionData(sysVar, "系统变量"));
            }

            // Hook up AvalonEdit events for autocomplete
            txtEditor.TextArea.TextEntering += TextArea_TextEntering;
            txtEditor.TextArea.KeyDown += TextArea_KeyDown;
            txtEditor.TextArea.Caret.PositionChanged += (s, e) => UpdateCaretInfo();

            // Setup real-time validation timer (500ms debounce)
            _validationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _validationTimer.Tick += ValidationTimer_Tick;

            txtEditor.TextChanged += TxtEditor_TextChanged;

            UpdateCaretInfo();

            // Initial validation
            ScheduleValidation();
        }

        // ==================== VARIABLES PANEL ====================

        /// <summary>Groups project variables and system variables into the variables list.</summary>
        private void PopulateVariables(List<string> availableVariables)
        {
            var items = new List<VariableItem>();
            if (availableVariables != null)
            {
                foreach (var v in availableVariables)
                {
                    items.Add(new VariableItem(v, "变量"));
                }
            }
            items.Add(new VariableItem("ProjectName", "系统变量"));
            items.Add(new VariableItem("Date", "系统变量"));
            items.Add(new VariableItem("User", "系统变量"));

            _allVariables = new ListCollectionView(items);
            _allVariables.GroupDescriptions.Add(new PropertyGroupDescription(nameof(VariableItem.Category)));
            lstVariables.ItemsSource = _allVariables;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtSearchWatermark != null)
            {
                txtSearchWatermark.Visibility = string.IsNullOrEmpty(txtSearch.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (_allVariables == null) return;
            string query = txtSearch.Text?.Trim() ?? "";
            _allVariables.Filter = o =>
            {
                if (o is VariableItem vi)
                {
                    return string.IsNullOrEmpty(query) ||
                           vi.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                return false;
            };
        }

        private void UpdateCaretInfo()
        {
            if (txtCaret == null) return;
            txtCaret.Text = $"行 {txtEditor.TextArea.Caret.Line} · 列 {txtEditor.TextArea.Caret.Column}";
        }

        // ==================== TOOLBAR ====================

        private void BuildToolbar()
        {
            if (toolbarPanel == null) return;

            var style = (Style)FindResource("ToolbarBtn");

            void AddButton(string label, string insert, string tooltip)
            {
                var btn = new Button
                {
                    Content = label,
                    Tag = insert,
                    ToolTip = tooltip,
                    Style = style
                };
                btn.Click += ToolbarBtn_Click;
                toolbarPanel.Children.Add(btn);
            }

            // Operators
            AddButton("(", "(", "左括号");
            AddButton(")", ")", "右括号");
            AddButton("+", " + ", "加");
            AddButton("-", " - ", "减");
            AddButton("*", " * ", "乘");
            AddButton("/", " / ", "除");
            AddButton("%", " % ", "取模");
            AddButton("==", " == ", "等于");
            AddButton("!=", " != ", "不等于");
            AddButton(">", " > ", "大于");
            AddButton("<", " < ", "小于");
            AddButton(">=", " >= ", "大于等于");
            AddButton("<=", " <= ", "小于等于");
            AddButton("&&", " && ", "逻辑与");
            AddButton("||", " || ", "逻辑或");
            AddButton("!", " ! ", "逻辑非");
            AddButton("?", " ? ", "三元运算符");
            AddButton(":", " : ", "三元分隔符");

            // Separator
            toolbarPanel.Children.Add(new Border
            {
                Width = 1,
                Height = 18,
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                Margin = new Thickness(2, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Functions
            AddButton("If", "If(条件, 真值, 假值)", "条件函数");
            AddButton("Concat", "Concat(a, b)", "字符串拼接");
            AddButton("Upper", "Upper(text)", "转大写");
            AddButton("Lower", "Lower(text)", "转小写");
            AddButton("Abs", "Abs(n)", "绝对值");
            AddButton("Round", "Round(n, 2)", "四舍五入");
            AddButton("Lookup", "Lookup('表名', '返回列', '键列', '键值')", "目录查询");
            AddButton("Lookup_Breaker", "Lookup_Breaker(功率, '启动方式')", "断路器选型");
        }

        private void ToolbarBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string insert)
            {
                txtEditor.Document.Insert(txtEditor.CaretOffset, insert);
                txtEditor.Focus();
            }
        }

        // ==================== NCalc SYNTAX HIGHLIGHTING ====================

        private static IHighlightingDefinition BuildNCalcHighlighting(IEnumerable<string> variableNames)
        {
            var xshd = new XshdSyntaxDefinition { Name = "NCalc" };

            var varColor = new XshdColor { Name = "Variable", Foreground = Brush("#56B6C2") };
            var numColor = new XshdColor { Name = "Number", Foreground = Brush("#D19A66") };
            var strColor = new XshdColor { Name = "String", Foreground = Brush("#98C379") };
            var funcColor = new XshdColor { Name = "Function", Foreground = Brush("#C678DD"), FontWeight = FontWeights.Bold };
            var opColor = new XshdColor { Name = "Operator", Foreground = Brush("#61AFEF") };

            xshd.Elements.Add(varColor);
            xshd.Elements.Add(numColor);
            xshd.Elements.Add(strColor);
            xshd.Elements.Add(funcColor);
            xshd.Elements.Add(opColor);

            var ruleSet = new XshdRuleSet();

            // Colors are registered as named elements above; rules reference them by name
            // (name-based references avoid double registration of the color objects).
            void AddRule(string colorName, string pattern)
            {
                ruleSet.Elements.Add(new XshdRule
                {
                    ColorReference = new XshdReference<XshdColor>(null, colorName),
                    Regex = pattern
                });
            }

            // String literals
            AddRule("String", @"'(?:[^']|'')*'|""(?:[^""\\]|\\.)*""");

            // Functions (custom + common NCalc built-ins) — (?i:...) makes the group case-insensitive
            AddRule("Function",
                @"\b(?i:Lookup_Breaker|Lookup|Concat|IsAvailable|IsNullEmpty|Contains|StartsWith|EndsWith|Upper|Lower|If|Abs|Floor|Ceiling|Round|Max|Min|Pow|Sqrt|Log|Log10|Exp|Sin|Cos|Tan|Sign|Truncate|In|NotIn|PI|E)\b");

            // Variables (dynamic, derived from the current project)
            var cleanVars = variableNames?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(Regex.Escape)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (cleanVars != null && cleanVars.Count > 0)
            {
                AddRule("Variable", $@"\b(?i:{string.Join("|", cleanVars)})\b");
            }

            // Numbers
            AddRule("Number", @"\b\d+(?:\.\d+)?\b");

            // Operators (longest match first for two-character operators)
            AddRule("Operator", @"==|!=|<=|>=|&&|\|\||[+\-*/%^<>!()?:]");

            xshd.Elements.Add(ruleSet);

            return HighlightingLoader.Load(xshd, HighlightingManager.Instance);
        }

        private static HighlightingBrush Brush(string hex)
        {
            return new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        // ==================== AUTOCOMPLETE ====================

        private void TextArea_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Space triggers completion manually
            if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                ShowCompletionWindow(manualTrigger: true);
                return;
            }

            // If completion window is open, Escape closes it
            if (_completionWindow != null && e.Key == Key.Escape)
            {
                _completionWindow.Close();
                e.Handled = true;
            }
        }

        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (_completionWindow != null && !string.IsNullOrEmpty(e.Text))
            {
                // If the character is not a letter/digit/underscore/dot, close completion
                char ch = e.Text[0];
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.')
                {
                    _completionWindow.CompletionList.RequestInsertion(e);
                }
            }

            // Auto-trigger completion on typing a letter or dot
            if (!string.IsNullOrEmpty(e.Text))
            {
                char ch = e.Text[0];
                if (char.IsLetter(ch) || ch == '_' || ch == '.')
                {
                    // Delay slightly to let the text be inserted first
                    Dispatcher.BeginInvoke(new Action(() => ShowCompletionWindow(manualTrigger: false)),
                        DispatcherPriority.Input);
                }
            }
        }

        private void ShowCompletionWindow(bool manualTrigger)
        {
            // Don't open if already open
            if (_completionWindow != null) return;

            // Get the current word being typed
            string currentWord = GetCurrentWord();

            if (!manualTrigger)
            {
                // Auto-trigger: only show if we have at least 1 character of a word
                if (string.IsNullOrEmpty(currentWord)) return;
            }

            // Filter completion items
            var filtered = string.IsNullOrEmpty(currentWord)
                ? _allCompletionItems
                : _allCompletionItems.Where(c =>
                    c.Text.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase) ||
                    c.Text.IndexOf(currentWord, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (filtered.Count == 0) return;

            // Create completion window
            _completionWindow = new CompletionWindow(txtEditor.TextArea);
            _completionWindow.CloseWhenCaretAtBeginning = true;

            foreach (var item in filtered)
            {
                _completionWindow.CompletionList.CompletionData.Add(item);
            }

            // Select first item if manual trigger
            if (manualTrigger && _completionWindow.CompletionList.CompletionData.Count > 0)
            {
                _completionWindow.CompletionList.ListBox.SelectedIndex = 0;
            }

            _completionWindow.Closed += (s, args) =>
            {
                _completionWindow = null;
            };

            _completionWindow.Show();
        }

        private string GetCurrentWord()
        {
            int offset = txtEditor.CaretOffset;
            if (offset <= 0) return "";

            int start = offset;
            while (start > 0)
            {
                char ch = txtEditor.Document.GetCharAt(start - 1);
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                {
                    start--;
                }
                else
                {
                    break;
                }
            }

            if (start == offset) return "";
            return txtEditor.Document.GetText(start, offset - start);
        }

        // ==================== REAL-TIME VALIDATION ====================

        private void TxtEditor_TextChanged(object sender, EventArgs e)
        {
            ScheduleValidation();
        }

        private void ScheduleValidation()
        {
            _validationTimer.Stop();
            _validationTimer.Start();
        }

        private void ValidationTimer_Tick(object sender, EventArgs e)
        {
            _validationTimer.Stop();
            ValidateCurrentExpression();
        }

        private void ValidateCurrentExpression()
        {
            string expression = txtEditor.Text;

            if (string.IsNullOrWhiteSpace(expression))
            {
                SetValidationState(ValidationState.Neutral);
                SetStatus("", Colors.Gray);
                return;
            }

            try
            {
                var mockParams = BuildMockParams();
                var result = ConfiguratorService.EvaluateExpression(expression, mockParams);
                SetValidationState(ValidationState.Valid);
                SetStatus($"语法通过 ✓ (模拟结果: {result})", Colors.LightGreen);
            }
            catch (Exception ex)
            {
                SetValidationState(ValidationState.Invalid, ex.Message);
                SetStatus($"语法错误 ✗", Colors.Red);
            }
        }

        private enum ValidationState { Neutral, Valid, Invalid }

        private void SetValidationState(ValidationState state, string errorMessage = null)
        {
            switch (state)
            {
                case ValidationState.Neutral:
                    editorBorder.BorderBrush = BorderDefault;
                    validationIcon.Text = "";
                    errorBorder.Visibility = Visibility.Collapsed;
                    break;
                case ValidationState.Valid:
                    editorBorder.BorderBrush = BorderValid;
                    validationIcon.Text = "✓";
                    validationIcon.Foreground = BorderValid;
                    errorBorder.Visibility = Visibility.Collapsed;
                    break;
                case ValidationState.Invalid:
                    editorBorder.BorderBrush = BorderInvalid;
                    validationIcon.Text = "✗";
                    validationIcon.Foreground = BorderInvalid;
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        errorBorder.Visibility = Visibility.Visible;
                        txtErrorDetail.Text = FormatErrorMessage(errorMessage);
                    }
                    else
                    {
                        errorBorder.Visibility = Visibility.Collapsed;
                    }
                    break;
            }
        }

        private string FormatErrorMessage(string rawMessage)
        {
            // Try to extract useful error info from Jint exception
            // Jint messages often contain: "message at position X line Y"
            string msg = rawMessage;

            // Strip the wrapping prefix if present
            int idx = msg.IndexOf("解析失败:");
            if (idx >= 0)
            {
                msg = msg.Substring(idx + "解析失败:".Length).Trim();
            }

            // Try to extract line/position info
            var lineMatch = System.Text.RegularExpressions.Regex.Match(msg, @"line\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var posMatch = System.Text.RegularExpressions.Regex.Match(msg, @"position\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            string location = "";
            if (lineMatch.Success)
                location += $"行 {lineMatch.Groups[1].Value}";
            if (posMatch.Success)
            {
                if (location.Length > 0) location += ", ";
                location += $"位置 {posMatch.Groups[1].Value}";
            }

            if (location.Length > 0)
            {
                return $"[{location}] {msg}";
            }

            return msg;
        }

        private Dictionary<string, object> BuildMockParams()
        {
            var mockParams = new Dictionary<string, object>();
            foreach (var v in _availableVariables)
            {
                mockParams[v] = 0;
            }
            return mockParams;
        }

        // ==================== EXISTING HANDLERS ====================

        private void LstVariables_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstVariables.SelectedItem is VariableItem item)
            {
                txtEditor.Document.Insert(txtEditor.CaretOffset, item.Name);
                txtEditor.Focus();
            }
        }

        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            string expression = txtEditor.Text;
            if (string.IsNullOrWhiteSpace(expression))
            {
                SetStatus("公式为空", Colors.Gray);
                SetValidationState(ValidationState.Neutral);
                return;
            }

            try
            {
                var mockParams = BuildMockParams();
                var result = ConfiguratorService.EvaluateExpression(expression, mockParams);
                SetStatus($"语法通过 ✓ (模拟结果: {result})", Colors.LightGreen);
                SetValidationState(ValidationState.Valid);
            }
            catch (Exception ex)
            {
                SetStatus($"语法错误 ✗ : {ex.Message}", Colors.Red);
                SetValidationState(ValidationState.Invalid, ex.Message);
            }
        }

        private void SetStatus(string msg, Color color)
        {
            txtStatus.Text = msg;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            txtStatus.Foreground = brush;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            FormulaText = txtEditor.Text;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // ==================== VARIABLE ITEM ====================

    /// <summary>
    /// Display item for the grouped variables panel (name + category tag).
    /// </summary>
    internal sealed class VariableItem
    {
        public VariableItem(string name, string category)
        {
            Name = name;
            Category = category;
        }

        public string Name { get; }
        public string Category { get; }
    }

    // ==================== COMPLETION DATA ====================

    /// <summary>
    /// AvalonEdit ICompletionData implementation for variables and functions.
    /// </summary>
    internal class VariableCompletionData : ICompletionData
    {
        private readonly object _cachedContent;

        public VariableCompletionData(string text, string category)
        {
            Text = text;
            Category = category;
            _cachedContent = BuildContent();
        }

        public System.Windows.Media.ImageSource Image => null;

        public string Text { get; }

        public string Category { get; }

        // Use Content for display with category tag
        public object Content => _cachedContent;

        private object BuildContent()
        {
            var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var nameBrush = new SolidColorBrush(Colors.White);
            nameBrush.Freeze();
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = Text,
                FontWeight = FontWeights.SemiBold,
                Foreground = nameBrush
            });
            var tagBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            tagBrush.Freeze();
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"  [{Category}]",
                FontSize = 11,
                Foreground = tagBrush,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            return panel;
        }

        public object Description => $"{Category}: {Text}";

        public double Priority => 0.5;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
