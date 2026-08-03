using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class FormulaEditorDialog : Window
    {
        public string FormulaText { get; private set; }

        private class BuiltInFunction
        {
            public string Name { get; set; }
            public string Template { get; set; }
            public string Description { get; set; }
        }

        // --- Autocomplete ---
        private CompletionWindow _completionWindow;
        private List<ICompletionData> _allCompletionItems;

        // --- Real-time validation ---
        private DispatcherTimer _validationTimer;
        private static readonly SolidColorBrush BorderDefault;
        private static readonly SolidColorBrush BorderValid;
        private static readonly SolidColorBrush BorderInvalid;

        static FormulaEditorDialog()
        {
            BorderDefault = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            BorderValid = new SolidColorBrush(Color.FromRgb(0x2D, 0x7D, 0x46));
            BorderInvalid = new SolidColorBrush(Color.FromRgb(0xE8, 0x40, 0x40));
            BorderDefault.Freeze();
            BorderValid.Freeze();
            BorderInvalid.Freeze();
        }

        private static readonly string[] BuiltInFunctionNames = new[]
        {
            "Lookup_Breaker", "Lookup", "Concat", "IsAvailable",
            "IsNullEmpty", "Contains", "StartsWith", "EndsWith",
            "Upper", "Lower", "If"
        };

        public FormulaEditorDialog(string initialText, List<string> availableVariables)
        {
            InitializeComponent();
            txtEditor.Text = initialText ?? "";

            // Populate variables
            lstVariables.ItemsSource = availableVariables;

            // Populate functions
            var functions = new List<BuiltInFunction>
            {
                new BuiltInFunction { Name = "Lookup_Breaker", Template = "Lookup_Breaker(Current, 'StartType')", Description = "断路器查表选型函数" },
                new BuiltInFunction { Name = "Lookup", Template = "Lookup('table_name.csv', 'returnCol', 'searchCol', 'searchVal')", Description = "通用CSV查表函数" },
                new BuiltInFunction { Name = "If", Template = "If(condition, trueValue, falseValue)", Description = "三元条件判断 (兼容版)" },
                new BuiltInFunction { Name = "IsAvailable", Template = "IsAvailable('VariableName')", Description = "检查变量是否存在且非空" },
                new BuiltInFunction { Name = "Concat", Template = "Concat(A, B, ...)", Description = "拼接多个字符串（支持2+参数）" },
                new BuiltInFunction { Name = "IsNullEmpty", Template = "IsNullEmpty(A)", Description = "判断是否为空" },
                new BuiltInFunction { Name = "Includes / Contains", Template = "A.includes(B)", Description = "JS原生: 检查A是否包含B" }
            };
            lstFunctions.ItemsSource = functions;

            // Build completion data list
            _allCompletionItems = new List<ICompletionData>();

            // Add variables
            if (availableVariables != null)
            {
                foreach (var v in availableVariables)
                {
                    _allCompletionItems.Add(new VariableCompletionData(v, "变量"));
                }
            }

            // Add system variables
            foreach (var sysVar in new[] { "ProjectName", "Date", "User" })
            {
                _allCompletionItems.Add(new VariableCompletionData(sysVar, "系统变量"));
            }

            // Add built-in functions
            foreach (var fn in BuiltInFunctionNames)
            {
                _allCompletionItems.Add(new VariableCompletionData(fn, "函数"));
            }

            // Hook up AvalonEdit events for autocomplete
            txtEditor.TextArea.TextEntering += TextArea_TextEntering;
            txtEditor.TextArea.KeyDown += TextArea_KeyDown;

            // Setup real-time validation timer (500ms debounce)
            _validationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _validationTimer.Tick += ValidationTimer_Tick;

            txtEditor.TextChanged += TxtEditor_TextChanged;

            // Initial validation
            ScheduleValidation();
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
            if (lstVariables.ItemsSource is List<string> vars)
            {
                foreach (var v in vars)
                {
                    mockParams[v] = 0;
                }
            }
            return mockParams;
        }

        // ==================== EXISTING HANDLERS ====================

        private void LstVariables_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstVariables.SelectedItem is string varName)
            {
                txtEditor.Document.Insert(txtEditor.CaretOffset, varName);
                txtEditor.Focus();
            }
        }

        private void LstFunctions_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (lstFunctions.SelectedItem is BuiltInFunction func)
            {
                txtEditor.Document.Insert(txtEditor.CaretOffset, func.Template);
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
            var nameBrush = new SolidColorBrush(Category == "函数"
                ? Color.FromRgb(0x00, 0x84, 0xFF)
                : Colors.White);
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

        public double Priority => Category == "函数" ? 1.0 : 0.5;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
