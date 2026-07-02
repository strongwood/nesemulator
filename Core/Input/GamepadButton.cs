namespace NESEmulator.Core.Input
{
    public enum GamepadButton
    {
        None = 0,
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight,
        ButtonA,
        ButtonB,
        ButtonX,
        ButtonY,
        LeftShoulder,
        RightShoulder,
        Back,
        Start,
        LeftTrigger,
        RightTrigger,
        LeftStickUp,
        LeftStickDown,
        LeftStickLeft,
        LeftStickRight
    }

    public static class GamepadButtonExtensions
    {
        public static string GetDisplayName(this GamepadButton button)
        {
            return button switch
            {
                GamepadButton.None => "未设置",
                GamepadButton.DPadUp => "方向键上",
                GamepadButton.DPadDown => "方向键下",
                GamepadButton.DPadLeft => "方向键左",
                GamepadButton.DPadRight => "方向键右",
                GamepadButton.ButtonA => "A",
                GamepadButton.ButtonB => "B",
                GamepadButton.ButtonX => "X",
                GamepadButton.ButtonY => "Y",
                GamepadButton.LeftShoulder => "LB",
                GamepadButton.RightShoulder => "RB",
                GamepadButton.Back => "Back",
                GamepadButton.Start => "Start",
                GamepadButton.LeftTrigger => "LT",
                GamepadButton.RightTrigger => "RT",
                GamepadButton.LeftStickUp => "左摇杆上",
                GamepadButton.LeftStickDown => "左摇杆下",
                GamepadButton.LeftStickLeft => "左摇杆左",
                GamepadButton.LeftStickRight => "左摇杆右",
                _ => button.ToString()
            };
        }
    }
}
