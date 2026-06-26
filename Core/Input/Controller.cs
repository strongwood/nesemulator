namespace NESEmulator.Core.Input
{
    public enum ControllerButton
    {
        A = 0,
        B = 1,
        Select = 2,
        Start = 3,
        Up = 4,
        Down = 5,
        Left = 6,
        Right = 7
    }

    public class Controller
    {
        private byte buttonStates = 0;
        private byte shiftRegister = 0;
        private bool strobe = false;

        public Controller()
        {
            Reset();
        }

        public void Reset()
        {
            buttonStates = 0;
            shiftRegister = 0;
            strobe = false;
        }

        public void SetButton(ControllerButton button, bool pressed)
        {
            int buttonIndex = (int)button;
            
            if (pressed)
            {
                buttonStates |= (byte)(1 << buttonIndex);
            }
            else
            {
                buttonStates &= (byte)~(1 << buttonIndex);
            }
        }

        public bool IsButtonPressed(ControllerButton button)
        {
            int buttonIndex = (int)button;
            return (buttonStates & (1 << buttonIndex)) != 0;
        }

        public void Write(byte value)
        {
            bool newStrobe = (value & 1) != 0;
            
            if (strobe && !newStrobe)
            {
                // 选通信号从高变低，锁存当前按钮状态
                shiftRegister = buttonStates;
            }
            
            strobe = newStrobe;
        }

        public byte Read()
        {
            byte result;
            
            if (strobe)
            {
                // 选通模式：总是返回A按钮状态
                result = (byte)((buttonStates & 1) != 0 ? 1 : 0);
            }
            else
            {
                // 移位模式：依次返回每个按钮状态
                result = (byte)((shiftRegister & 1) != 0 ? 1 : 0);
                shiftRegister >>= 1;
                shiftRegister |= 0x80; // 高位填充1
            }
            
            return result;
        }

        public byte GetButtonStates()
        {
            return buttonStates;
        }

        public void SetButtonStates(byte states)
        {
            buttonStates = states;
        }
    }
}