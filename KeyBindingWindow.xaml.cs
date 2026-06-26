using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NESEmulator.Core.Input;

namespace NESEmulator
{
    public partial class KeyBindingWindow : Window
    {
        private readonly Dictionary<ControllerButton, TextBox> bindingTextBoxes;
        private readonly int playerIndex;
        private KeyboardMapping editableMapping;
        private ControllerButton? pendingButton;
        private readonly string defaultInstructionText;

        public KeyBindingWindow(KeyboardMapping currentMapping, int playerIndex)
        {
            InitializeComponent();

            this.playerIndex = playerIndex;
            editableMapping = currentMapping.Clone();
            Title = $"玩家{playerIndex + 1}按键设置";
            defaultInstructionText = $"设置玩家{playerIndex + 1}的方向键和功能键。点击“修改”后按下新的按键。";
            InstructionText.Text = defaultInstructionText;
            bindingTextBoxes = new Dictionary<ControllerButton, TextBox>
            {
                [ControllerButton.Up] = UpKeyTextBox,
                [ControllerButton.Down] = DownKeyTextBox,
                [ControllerButton.Left] = LeftKeyTextBox,
                [ControllerButton.Right] = RightKeyTextBox,
                [ControllerButton.A] = AKeyTextBox,
                [ControllerButton.B] = BKeyTextBox,
                [ControllerButton.Select] = SelectKeyTextBox,
                [ControllerButton.Start] = StartKeyTextBox
            };

            PreviewKeyDown += KeyBindingWindow_PreviewKeyDown;
            RefreshBindingText();
        }

        public KeyboardMapping ResultMapping => editableMapping.Clone();

        private void ChangeBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string buttonName)
            {
                return;
            }

            if (!Enum.TryParse(buttonName, out ControllerButton controllerButton))
            {
                return;
            }

            pendingButton = controllerButton;
            InstructionText.Text = $"正在设置 {GetButtonDisplayName(controllerButton)}，请按下新的按键，按 Esc 取消。";
        }

        private void KeyBindingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (pendingButton == null)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                pendingButton = null;
                InstructionText.Text = defaultInstructionText;
                e.Handled = true;
                return;
            }

            editableMapping.SetBinding(pendingButton.Value, key);
            pendingButton = null;
            RefreshBindingText();
            InstructionText.Text = defaultInstructionText;
            e.Handled = true;
        }

        private void ResetDefault_Click(object sender, RoutedEventArgs e)
        {
            editableMapping = KeyboardMapping.CreateDefault(playerIndex);
            pendingButton = null;
            RefreshBindingText();
            InstructionText.Text = $"已恢复玩家{playerIndex + 1}默认按键。";
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RefreshBindingText()
        {
            foreach (KeyValuePair<ControllerButton, TextBox> pair in bindingTextBoxes)
            {
                Key key = editableMapping.GetKey(pair.Key);
                pair.Value.Text = key == Key.None ? "未设置" : key.ToString();
            }
        }

        private static string GetButtonDisplayName(ControllerButton button)
        {
            return button switch
            {
                ControllerButton.Up => "上",
                ControllerButton.Down => "下",
                ControllerButton.Left => "左",
                ControllerButton.Right => "右",
                ControllerButton.A => "A 键",
                ControllerButton.B => "B 键",
                ControllerButton.Select => "Select",
                ControllerButton.Start => "Start",
                _ => button.ToString()
            };
        }
    }
}
