using System.Runtime.InteropServices;

namespace NESEmulator.Core.Input
{
    public static class XInputGamepad
    {
        private const uint ErrorSuccess = 0;
        private const ushort DPadUpFlag = 0x0001;
        private const ushort DPadDownFlag = 0x0002;
        private const ushort DPadLeftFlag = 0x0004;
        private const ushort DPadRightFlag = 0x0008;
        private const ushort StartFlag = 0x0010;
        private const ushort BackFlag = 0x0020;
        private const ushort LeftShoulderFlag = 0x0100;
        private const ushort RightShoulderFlag = 0x0200;
        private const ushort ButtonAFlag = 0x1000;
        private const ushort ButtonBFlag = 0x2000;
        private const ushort ButtonXFlag = 0x4000;
        private const ushort ButtonYFlag = 0x8000;
        private const byte TriggerThreshold = 30;
        private const short ThumbAxisThreshold = 16000;
        private static readonly string[] CandidateLibraries = ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"];
        private static readonly GamepadButton[] OrderedButtons =
        [
            GamepadButton.DPadUp,
            GamepadButton.DPadDown,
            GamepadButton.DPadLeft,
            GamepadButton.DPadRight,
            GamepadButton.ButtonA,
            GamepadButton.ButtonB,
            GamepadButton.ButtonX,
            GamepadButton.ButtonY,
            GamepadButton.LeftShoulder,
            GamepadButton.RightShoulder,
            GamepadButton.Back,
            GamepadButton.Start,
            GamepadButton.LeftTrigger,
            GamepadButton.RightTrigger,
            GamepadButton.LeftStickUp,
            GamepadButton.LeftStickDown,
            GamepadButton.LeftStickLeft,
            GamepadButton.LeftStickRight
        ];

        private static readonly XInputGetStateDelegate? GetStateDelegate = LoadGetStateDelegate();

        public static bool IsSupported => GetStateDelegate != null;

        public static bool TryGetState(int deviceIndex, out Snapshot snapshot)
        {
            snapshot = default;

            if (GetStateDelegate == null || deviceIndex < 0 || deviceIndex > 3)
            {
                return false;
            }

            uint result = GetStateDelegate((uint)deviceIndex, out XInputState state);
            if (result != ErrorSuccess)
            {
                return false;
            }

            snapshot = new Snapshot(state.Gamepad);
            return true;
        }

        public static List<GamepadButton> GetPressedButtons(Snapshot snapshot)
        {
            var pressedButtons = new List<GamepadButton>();

            foreach (GamepadButton button in OrderedButtons)
            {
                if (snapshot.IsPressed(button))
                {
                    pressedButtons.Add(button);
                }
            }

            return pressedButtons;
        }

        private static XInputGetStateDelegate? LoadGetStateDelegate()
        {
            foreach (string libraryName in CandidateLibraries)
            {
                if (!NativeLibrary.TryLoad(libraryName, out nint libraryHandle))
                {
                    continue;
                }

                if (!NativeLibrary.TryGetExport(libraryHandle, "XInputGetState", out nint exportHandle))
                {
                    continue;
                }

                return Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(exportHandle);
            }

            return null;
        }

        public readonly struct Snapshot
        {
            private readonly ushort buttons;
            private readonly byte leftTrigger;
            private readonly byte rightTrigger;
            private readonly short leftThumbX;
            private readonly short leftThumbY;

            internal Snapshot(XInputGamepadState nativeState)
            {
                buttons = nativeState.wButtons;
                leftTrigger = nativeState.bLeftTrigger;
                rightTrigger = nativeState.bRightTrigger;
                leftThumbX = nativeState.sThumbLX;
                leftThumbY = nativeState.sThumbLY;
            }

            public bool IsPressed(GamepadButton button)
            {
                return button switch
                {
                    GamepadButton.DPadUp => (buttons & DPadUpFlag) != 0,
                    GamepadButton.DPadDown => (buttons & DPadDownFlag) != 0,
                    GamepadButton.DPadLeft => (buttons & DPadLeftFlag) != 0,
                    GamepadButton.DPadRight => (buttons & DPadRightFlag) != 0,
                    GamepadButton.ButtonA => (buttons & ButtonAFlag) != 0,
                    GamepadButton.ButtonB => (buttons & ButtonBFlag) != 0,
                    GamepadButton.ButtonX => (buttons & ButtonXFlag) != 0,
                    GamepadButton.ButtonY => (buttons & ButtonYFlag) != 0,
                    GamepadButton.LeftShoulder => (buttons & LeftShoulderFlag) != 0,
                    GamepadButton.RightShoulder => (buttons & RightShoulderFlag) != 0,
                    GamepadButton.Back => (buttons & BackFlag) != 0,
                    GamepadButton.Start => (buttons & StartFlag) != 0,
                    GamepadButton.LeftTrigger => leftTrigger >= TriggerThreshold,
                    GamepadButton.RightTrigger => rightTrigger >= TriggerThreshold,
                    GamepadButton.LeftStickUp => leftThumbY >= ThumbAxisThreshold,
                    GamepadButton.LeftStickDown => leftThumbY <= -ThumbAxisThreshold,
                    GamepadButton.LeftStickLeft => leftThumbX <= -ThumbAxisThreshold,
                    GamepadButton.LeftStickRight => leftThumbX >= ThumbAxisThreshold,
                    _ => false
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XInputState
        {
            public uint dwPacketNumber;
            public XInputGamepadState Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct XInputGamepadState
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint XInputGetStateDelegate(uint userIndex, out XInputState state);
    }
}
