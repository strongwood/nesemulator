using System.IO;
using System.Text.Json;

namespace NESEmulator.Core.Input
{
    public sealed class GamepadMapping
    {
        private readonly Dictionary<ControllerButton, GamepadButton> bindings;
        private readonly Dictionary<TurboButton, GamepadButton> turboBindings;

        public GamepadMapping()
        {
            bindings = new Dictionary<ControllerButton, GamepadButton>();
            turboBindings = new Dictionary<TurboButton, GamepadButton>();
            DeviceIndex = 0;
        }

        public int DeviceIndex { get; set; }

        public static GamepadMapping CreateDefault(int playerIndex = 0)
        {
            var mapping = new GamepadMapping
            {
                DeviceIndex = Math.Clamp(playerIndex, 0, 3)
            };

            mapping.bindings[ControllerButton.Up] = GamepadButton.DPadUp;
            mapping.bindings[ControllerButton.Down] = GamepadButton.DPadDown;
            mapping.bindings[ControllerButton.Left] = GamepadButton.DPadLeft;
            mapping.bindings[ControllerButton.Right] = GamepadButton.DPadRight;
            mapping.bindings[ControllerButton.A] = GamepadButton.ButtonB;
            mapping.bindings[ControllerButton.B] = GamepadButton.ButtonA;
            mapping.bindings[ControllerButton.Select] = GamepadButton.Back;
            mapping.bindings[ControllerButton.Start] = GamepadButton.Start;

            return mapping;
        }

        public GamepadButton GetButton(ControllerButton button)
        {
            return bindings.TryGetValue(button, out GamepadButton value) ? value : GamepadButton.None;
        }

        public IReadOnlyDictionary<ControllerButton, GamepadButton> GetBindings()
        {
            return bindings;
        }

        public GamepadButton GetTurboButton(TurboButton button)
        {
            return turboBindings.TryGetValue(button, out GamepadButton value) ? value : GamepadButton.None;
        }

        public void SetBinding(ControllerButton button, GamepadButton inputButton)
        {
            ClearDuplicateGamepadBindings(inputButton, button, null);

            bindings[button] = inputButton;
        }

        public void SetTurboBinding(TurboButton button, GamepadButton inputButton)
        {
            ClearDuplicateGamepadBindings(inputButton, null, button);
            turboBindings[button] = inputButton;
        }

        public bool TryGetButton(GamepadButton inputButton, out ControllerButton button)
        {
            foreach (KeyValuePair<ControllerButton, GamepadButton> pair in bindings)
            {
                if (pair.Value == inputButton)
                {
                    button = pair.Key;
                    return true;
                }
            }

            button = default;
            return false;
        }

        public GamepadMapping Clone()
        {
            var clone = new GamepadMapping
            {
                DeviceIndex = DeviceIndex
            };

            foreach (KeyValuePair<ControllerButton, GamepadButton> pair in bindings)
            {
                clone.bindings[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<TurboButton, GamepadButton> pair in turboBindings)
            {
                clone.turboBindings[pair.Key] = pair.Value;
            }

            return clone;
        }

        public static GamepadMapping Load(string filePath, int playerIndex = 0)
        {
            if (!File.Exists(filePath))
            {
                return CreateDefault(playerIndex);
            }

            try
            {
                string json = File.ReadAllText(filePath);
                SerializedGamepadMapping? serialized = JsonSerializer.Deserialize<SerializedGamepadMapping>(json);
                if (serialized == null)
                {
                    return CreateDefault(playerIndex);
                }

                var mapping = CreateDefault(playerIndex);
                mapping.DeviceIndex = Math.Clamp(serialized.DeviceIndex, 0, 3);

                if (serialized.Bindings != null)
                {
                    foreach (KeyValuePair<string, string> pair in serialized.Bindings)
                    {
                        if (!Enum.TryParse(pair.Key, out ControllerButton button))
                        {
                            continue;
                        }

                        if (!Enum.TryParse(pair.Value, out GamepadButton inputButton))
                        {
                            continue;
                        }

                        mapping.bindings[button] = inputButton;
                    }
                }

                if (serialized.TurboBindings != null)
                {
                    foreach (KeyValuePair<string, string> pair in serialized.TurboBindings)
                    {
                        if (!Enum.TryParse(pair.Key, out TurboButton button))
                        {
                            continue;
                        }

                        if (!Enum.TryParse(pair.Value, out GamepadButton inputButton))
                        {
                            continue;
                        }

                        mapping.turboBindings[button] = inputButton;
                    }
                }

                return mapping;
            }
            catch
            {
                return CreateDefault(playerIndex);
            }
        }

        public void Save(string filePath)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var serialized = new SerializedGamepadMapping
            {
                DeviceIndex = Math.Clamp(DeviceIndex, 0, 3),
                Bindings = bindings.ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => pair.Value.ToString()),
                TurboBindings = turboBindings.ToDictionary(
                    pair => pair.Key.ToString(),
                    pair => pair.Value.ToString())
            };

            string json = JsonSerializer.Serialize(serialized, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json);
        }

        private void ClearDuplicateGamepadBindings(GamepadButton inputButton, ControllerButton? exemptButton, TurboButton? exemptTurboButton)
        {
            if (inputButton == GamepadButton.None)
            {
                return;
            }

            List<ControllerButton> duplicateButtons = bindings
                .Where(pair => pair.Value == inputButton && pair.Key != exemptButton)
                .Select(pair => pair.Key)
                .ToList();

            foreach (ControllerButton duplicate in duplicateButtons)
            {
                bindings[duplicate] = GamepadButton.None;
            }

            List<TurboButton> duplicateTurboButtons = turboBindings
                .Where(pair => pair.Value == inputButton && pair.Key != exemptTurboButton)
                .Select(pair => pair.Key)
                .ToList();

            foreach (TurboButton duplicate in duplicateTurboButtons)
            {
                turboBindings[duplicate] = GamepadButton.None;
            }
        }

        private sealed class SerializedGamepadMapping
        {
            public int DeviceIndex { get; set; }
            public Dictionary<string, string>? Bindings { get; set; }
            public Dictionary<string, string>? TurboBindings { get; set; }
        }
    }
}
