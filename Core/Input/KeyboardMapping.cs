using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace NESEmulator.Core.Input
{
    public sealed class KeyboardMapping
    {
        private readonly Dictionary<ControllerButton, Key> bindings;
        private readonly Dictionary<TurboButton, Key> turboBindings;

        public KeyboardMapping()
        {
            bindings = new Dictionary<ControllerButton, Key>();
            turboBindings = new Dictionary<TurboButton, Key>();
        }

        public static KeyboardMapping CreateDefault(int playerIndex = 0)
        {
            var mapping = new KeyboardMapping();
            if (playerIndex == 0)
            {
                mapping.bindings[ControllerButton.Up] = Key.W;
                mapping.bindings[ControllerButton.Down] = Key.S;
                mapping.bindings[ControllerButton.Left] = Key.A;
                mapping.bindings[ControllerButton.Right] = Key.D;
                mapping.bindings[ControllerButton.A] = Key.J;
                mapping.bindings[ControllerButton.B] = Key.K;
                mapping.bindings[ControllerButton.Select] = Key.LeftShift;
                mapping.bindings[ControllerButton.Start] = Key.Space;
            }
            else
            {
                mapping.bindings[ControllerButton.Up] = Key.Up;
                mapping.bindings[ControllerButton.Down] = Key.Down;
                mapping.bindings[ControllerButton.Left] = Key.Left;
                mapping.bindings[ControllerButton.Right] = Key.Right;
                mapping.bindings[ControllerButton.A] = Key.Z;
                mapping.bindings[ControllerButton.B] = Key.X;
                mapping.bindings[ControllerButton.Select] = Key.RightShift;
                mapping.bindings[ControllerButton.Start] = Key.Enter;
            }

            return mapping;
        }

        public Key GetKey(ControllerButton button)
        {
            return bindings.TryGetValue(button, out Key key) ? key : Key.None;
        }

        public IReadOnlyDictionary<ControllerButton, Key> GetBindings()
        {
            return bindings;
        }

        public Key GetTurboKey(TurboButton button)
        {
            return turboBindings.TryGetValue(button, out Key key) ? key : Key.None;
        }

        public void SetBinding(ControllerButton button, Key key)
        {
            ClearDuplicateKeyBindings(key, button, null);

            bindings[button] = key;
        }

        public void SetTurboBinding(TurboButton button, Key key)
        {
            ClearDuplicateKeyBindings(key, null, button);
            turboBindings[button] = key;
        }

        public bool TryGetButton(Key key, out ControllerButton button)
        {
            foreach (KeyValuePair<ControllerButton, Key> pair in bindings)
            {
                if (pair.Value == key)
                {
                    button = pair.Key;
                    return true;
                }
            }

            button = default;
            return false;
        }

        public bool TryGetTurboButton(Key key, out TurboButton button)
        {
            foreach (KeyValuePair<TurboButton, Key> pair in turboBindings)
            {
                if (pair.Value == key)
                {
                    button = pair.Key;
                    return true;
                }
            }

            button = default;
            return false;
        }

        public KeyboardMapping Clone()
        {
            var clone = new KeyboardMapping();
            foreach (KeyValuePair<ControllerButton, Key> pair in bindings)
            {
                clone.bindings[pair.Key] = pair.Value;
            }

            foreach (KeyValuePair<TurboButton, Key> pair in turboBindings)
            {
                clone.turboBindings[pair.Key] = pair.Value;
            }

            return clone;
        }

        public static KeyboardMapping Load(string filePath, int playerIndex = 0)
        {
            if (!File.Exists(filePath))
            {
                return CreateDefault(playerIndex);
            }

            try
            {
                string json = File.ReadAllText(filePath);
                SerializedKeyboardMapping? serialized = JsonSerializer.Deserialize<SerializedKeyboardMapping>(json);
                if (serialized?.Bindings != null)
                {
                    return DeserializeStructuredMapping(serialized, playerIndex);
                }

                Dictionary<string, string>? legacySerialized = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (legacySerialized == null)
                {
                    return CreateDefault(playerIndex);
                }

                var mapping = CreateDefault(playerIndex);
                foreach (KeyValuePair<string, string> pair in legacySerialized)
                {
                    if (!Enum.TryParse(pair.Key, out ControllerButton button))
                    {
                        continue;
                    }

                    if (!Enum.TryParse(pair.Value, out Key key))
                    {
                        continue;
                    }

                    mapping.bindings[button] = key;
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

            Dictionary<string, string> serialized = bindings.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value.ToString());

            var turboSerialized = turboBindings.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value.ToString());

            var payload = new SerializedKeyboardMapping
            {
                Bindings = serialized,
                TurboBindings = turboSerialized
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json);
        }

        private void ClearDuplicateKeyBindings(Key key, ControllerButton? exemptButton, TurboButton? exemptTurboButton)
        {
            List<ControllerButton> duplicateButtons = bindings
                .Where(pair => pair.Value == key && pair.Key != exemptButton)
                .Select(pair => pair.Key)
                .ToList();

            foreach (ControllerButton duplicate in duplicateButtons)
            {
                bindings[duplicate] = Key.None;
            }

            List<TurboButton> duplicateTurboButtons = turboBindings
                .Where(pair => pair.Value == key && pair.Key != exemptTurboButton)
                .Select(pair => pair.Key)
                .ToList();

            foreach (TurboButton duplicate in duplicateTurboButtons)
            {
                turboBindings[duplicate] = Key.None;
            }
        }

        private static KeyboardMapping DeserializeStructuredMapping(SerializedKeyboardMapping serialized, int playerIndex)
        {
            var mapping = CreateDefault(playerIndex);

            foreach (KeyValuePair<string, string> pair in serialized.Bindings ?? [])
            {
                if (!Enum.TryParse(pair.Key, out ControllerButton button))
                {
                    continue;
                }

                if (!Enum.TryParse(pair.Value, out Key key))
                {
                    continue;
                }

                mapping.bindings[button] = key;
            }

            foreach (KeyValuePair<string, string> pair in serialized.TurboBindings ?? [])
            {
                if (!Enum.TryParse(pair.Key, out TurboButton button))
                {
                    continue;
                }

                if (!Enum.TryParse(pair.Value, out Key key))
                {
                    continue;
                }

                mapping.turboBindings[button] = key;
            }

            return mapping;
        }

        private sealed class SerializedKeyboardMapping
        {
            public Dictionary<string, string>? Bindings { get; set; }
            public Dictionary<string, string>? TurboBindings { get; set; }
        }
    }
}
