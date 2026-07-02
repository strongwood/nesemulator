using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NESEmulator.Core.Input;

namespace NESEmulator
{
    public partial class KeyBindingWindow : Window
    {
        private readonly Dictionary<ControllerButton, TextBox> keyboardTextBoxes;
        private readonly Dictionary<ControllerButton, TextBox> gamepadTextBoxes;
        private readonly Dictionary<TurboButton, TextBox> keyboardTurboTextBoxes;
        private readonly Dictionary<TurboButton, TextBox> gamepadTurboTextBoxes;
        private readonly int playerIndex;
        private KeyboardMapping editableKeyboardMapping;
        private GamepadMapping editableGamepadMapping;
        private ControllerButton? pendingKeyboardButton;
        private ControllerButton? pendingGamepadButton;
        private TurboButton? pendingKeyboardTurboButton;
        private TurboButton? pendingGamepadTurboButton;
        private readonly string defaultInstructionText;
        private readonly DispatcherTimer gamepadPollingTimer;
        private readonly HashSet<GamepadButton> blockedGamepadButtons = new HashSet<GamepadButton>();

        public KeyBindingWindow(KeyboardMapping currentKeyboardMapping, GamepadMapping currentGamepadMapping, int playerIndex)
        {
            InitializeComponent();

            this.playerIndex = playerIndex;
            editableKeyboardMapping = currentKeyboardMapping.Clone();
            editableGamepadMapping = currentGamepadMapping.Clone();
            Title = $"玩家{playerIndex + 1}输入设置";
            defaultInstructionText = $"设置玩家{playerIndex + 1}的键盘和手柄输入。点击“修改”后按下新的按键或手柄按键。";
            InstructionText.Text = defaultInstructionText;
            keyboardTextBoxes = new Dictionary<ControllerButton, TextBox>
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
            gamepadTextBoxes = new Dictionary<ControllerButton, TextBox>
            {
                [ControllerButton.Up] = GamepadUpTextBox,
                [ControllerButton.Down] = GamepadDownTextBox,
                [ControllerButton.Left] = GamepadLeftTextBox,
                [ControllerButton.Right] = GamepadRightTextBox,
                [ControllerButton.A] = GamepadATextBox,
                [ControllerButton.B] = GamepadBTextBox,
                [ControllerButton.Select] = GamepadSelectTextBox,
                [ControllerButton.Start] = GamepadStartTextBox
            };
            keyboardTurboTextBoxes = new Dictionary<TurboButton, TextBox>
            {
                [TurboButton.A] = TurboAKeyTextBox,
                [TurboButton.B] = TurboBKeyTextBox
            };
            gamepadTurboTextBoxes = new Dictionary<TurboButton, TextBox>
            {
                [TurboButton.A] = GamepadTurboATextBox,
                [TurboButton.B] = GamepadTurboBTextBox
            };

            PreviewKeyDown += KeyBindingWindow_PreviewKeyDown;
            GamepadDeviceComboBox.ItemsSource = Enumerable.Range(1, 4).Select(index => $"手柄 {index}").ToList();
            GamepadDeviceComboBox.SelectedIndex = Math.Clamp(editableGamepadMapping.DeviceIndex, 0, 3);
            gamepadPollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            gamepadPollingTimer.Tick += GamepadPollingTimer_Tick;
            gamepadPollingTimer.Start();
            Closed += KeyBindingWindow_Closed;
            RefreshBindingText();
            UpdateGamepadStatus();
        }

        public KeyboardMapping ResultKeyboardMapping => editableKeyboardMapping.Clone();
        public GamepadMapping ResultGamepadMapping => editableGamepadMapping.Clone();

        private void ChangeKeyboardBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string buttonName)
            {
                return;
            }

            if (!Enum.TryParse(buttonName, out ControllerButton controllerButton))
            {
                return;
            }

            pendingGamepadButton = null;
            pendingKeyboardTurboButton = null;
            pendingGamepadTurboButton = null;
            blockedGamepadButtons.Clear();
            pendingKeyboardButton = controllerButton;
            InstructionText.Text = $"正在设置键盘 {GetButtonDisplayName(controllerButton)}，请按下新的按键，按 Esc 取消。";
        }

        private void ChangeKeyboardTurboBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string buttonName)
            {
                return;
            }

            if (!Enum.TryParse(buttonName, out TurboButton turboButton))
            {
                return;
            }

            pendingKeyboardButton = null;
            pendingGamepadButton = null;
            pendingGamepadTurboButton = null;
            blockedGamepadButtons.Clear();
            pendingKeyboardTurboButton = turboButton;
            InstructionText.Text = $"正在设置键盘 {GetTurboButtonDisplayName(turboButton)}，请按下新的按键，按 Esc 取消。";
        }

        private void ChangeGamepadBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string buttonName)
            {
                return;
            }

            if (!Enum.TryParse(buttonName, out ControllerButton controllerButton))
            {
                return;
            }

            pendingKeyboardButton = null;
            pendingKeyboardTurboButton = null;
            pendingGamepadButton = controllerButton;
            pendingGamepadTurboButton = null;
            blockedGamepadButtons.Clear();

            foreach (GamepadButton pressedButton in GetPressedButtonsForSelectedGamepad())
            {
                blockedGamepadButtons.Add(pressedButton);
            }

            InstructionText.Text = $"正在设置手柄 {GetButtonDisplayName(controllerButton)}，请按下手柄按键，按 Esc 取消。";
        }

        private void ChangeGamepadTurboBinding_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string buttonName)
            {
                return;
            }

            if (!Enum.TryParse(buttonName, out TurboButton turboButton))
            {
                return;
            }

            pendingKeyboardButton = null;
            pendingKeyboardTurboButton = null;
            pendingGamepadButton = null;
            pendingGamepadTurboButton = turboButton;
            blockedGamepadButtons.Clear();

            foreach (GamepadButton pressedButton in GetPressedButtonsForSelectedGamepad())
            {
                blockedGamepadButtons.Add(pressedButton);
            }

            InstructionText.Text = $"正在设置手柄 {GetTurboButtonDisplayName(turboButton)}，请按下手柄按键，按 Esc 取消。";
        }

        private void KeyBindingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (pendingKeyboardButton == null &&
                pendingGamepadButton == null &&
                pendingKeyboardTurboButton == null &&
                pendingGamepadTurboButton == null)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                pendingKeyboardButton = null;
                pendingGamepadButton = null;
                pendingKeyboardTurboButton = null;
                pendingGamepadTurboButton = null;
                blockedGamepadButtons.Clear();
                InstructionText.Text = defaultInstructionText;
                e.Handled = true;
                return;
            }

            if (pendingKeyboardButton != null)
            {
                editableKeyboardMapping.SetBinding(pendingKeyboardButton.Value, key);
                pendingKeyboardButton = null;
                RefreshBindingText();
                InstructionText.Text = defaultInstructionText;
                e.Handled = true;
                return;
            }

            if (pendingKeyboardTurboButton != null)
            {
                editableKeyboardMapping.SetTurboBinding(pendingKeyboardTurboButton.Value, key);
                pendingKeyboardTurboButton = null;
                RefreshBindingText();
                InstructionText.Text = defaultInstructionText;
                e.Handled = true;
            }
        }

        private void ResetDefault_Click(object sender, RoutedEventArgs e)
        {
            editableKeyboardMapping = KeyboardMapping.CreateDefault(playerIndex);
            editableGamepadMapping = GamepadMapping.CreateDefault(playerIndex);
            pendingKeyboardButton = null;
            pendingGamepadButton = null;
            pendingKeyboardTurboButton = null;
            pendingGamepadTurboButton = null;
            blockedGamepadButtons.Clear();
            GamepadDeviceComboBox.SelectedIndex = editableGamepadMapping.DeviceIndex;
            RefreshBindingText();
            UpdateGamepadStatus();
            InstructionText.Text = $"已恢复玩家{playerIndex + 1}默认输入。";
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
            foreach (KeyValuePair<ControllerButton, TextBox> pair in keyboardTextBoxes)
            {
                Key key = editableKeyboardMapping.GetKey(pair.Key);
                pair.Value.Text = key == Key.None ? "未设置" : key.ToString();
            }

            foreach (KeyValuePair<ControllerButton, TextBox> pair in gamepadTextBoxes)
            {
                GamepadButton button = editableGamepadMapping.GetButton(pair.Key);
                pair.Value.Text = button.GetDisplayName();
            }

            foreach (KeyValuePair<TurboButton, TextBox> pair in keyboardTurboTextBoxes)
            {
                Key key = editableKeyboardMapping.GetTurboKey(pair.Key);
                pair.Value.Text = key == Key.None ? "未设置" : key.ToString();
            }

            foreach (KeyValuePair<TurboButton, TextBox> pair in gamepadTurboTextBoxes)
            {
                GamepadButton button = editableGamepadMapping.GetTurboButton(pair.Key);
                pair.Value.Text = button.GetDisplayName();
            }
        }

        private void GamepadDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || GamepadDeviceComboBox.SelectedIndex < 0)
            {
                return;
            }

            editableGamepadMapping.DeviceIndex = GamepadDeviceComboBox.SelectedIndex;
            blockedGamepadButtons.Clear();
            UpdateGamepadStatus();
        }

        private void GamepadPollingTimer_Tick(object? sender, EventArgs e)
        {
            UpdateGamepadStatus();

            if (pendingGamepadButton == null && pendingGamepadTurboButton == null)
            {
                return;
            }

            HashSet<GamepadButton> pressedButtons = GetPressedButtonsForSelectedGamepad();
            blockedGamepadButtons.IntersectWith(pressedButtons);

            GamepadButton capturedButton = pressedButtons.FirstOrDefault(button => !blockedGamepadButtons.Contains(button));
            if (capturedButton == GamepadButton.None)
            {
                return;
            }

            if (pendingGamepadButton != null)
            {
                editableGamepadMapping.SetBinding(pendingGamepadButton.Value, capturedButton);
                pendingGamepadButton = null;
            }
            else if (pendingGamepadTurboButton != null)
            {
                editableGamepadMapping.SetTurboBinding(pendingGamepadTurboButton.Value, capturedButton);
                pendingGamepadTurboButton = null;
            }

            blockedGamepadButtons.Clear();
            RefreshBindingText();
            InstructionText.Text = defaultInstructionText;
        }

        private HashSet<GamepadButton> GetPressedButtonsForSelectedGamepad()
        {
            var pressedButtons = new HashSet<GamepadButton>();
            if (XInputGamepad.TryGetState(GetSelectedGamepadDeviceIndex(), out XInputGamepad.Snapshot snapshot))
            {
                foreach (GamepadButton pressedButton in XInputGamepad.GetPressedButtons(snapshot))
                {
                    pressedButtons.Add(pressedButton);
                }
            }

            return pressedButtons;
        }

        private int GetSelectedGamepadDeviceIndex()
        {
            return GamepadDeviceComboBox.SelectedIndex >= 0
                ? GamepadDeviceComboBox.SelectedIndex
                : Math.Clamp(editableGamepadMapping.DeviceIndex, 0, 3);
        }

        private void UpdateGamepadStatus()
        {
            int deviceIndex = GetSelectedGamepadDeviceIndex();

            if (!XInputGamepad.IsSupported)
            {
                GamepadStatusText.Text = "系统未提供 XInput";
                return;
            }

            GamepadStatusText.Text = XInputGamepad.TryGetState(deviceIndex, out _)
                ? "已连接"
                : "未连接";
        }

        private void KeyBindingWindow_Closed(object? sender, EventArgs e)
        {
            gamepadPollingTimer.Stop();
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

        private static string GetTurboButtonDisplayName(TurboButton button)
        {
            return button switch
            {
                TurboButton.A => "连击 A",
                TurboButton.B => "连击 B",
                _ => button.ToString()
            };
        }
    }
}
