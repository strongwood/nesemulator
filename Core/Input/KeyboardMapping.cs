using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace NESEmulator.Core.Input
{
    public sealed class KeyboardMapping
    {
        private readonly Dictionary<ControllerButton, Key> bindings;

        public KeyboardMapping()
        {
            bindings = new Dictionary<ControllerButton, Key>();
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

        public void SetBinding(ControllerButton button, Key key)
        {
            List<ControllerButton> duplicates = bindings
                .Where(pair => pair.Key != button && pair.Value == key)
                .Select(pair => pair.Key)
                .ToList();

            foreach (ControllerButton duplicate in duplicates)
            {
                bindings[duplicate] = Key.None;
            }

            bindings[button] = key;
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

        public KeyboardMapping Clone()
        {
            var clone = new KeyboardMapping();
            foreach (KeyValuePair<ControllerButton, Key> pair in bindings)
            {
                clone.bindings[pair.Key] = pair.Value;
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
                Dictionary<string, string>? serialized = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (serialized == null)
                {
                    return CreateDefault(playerIndex);
                }

                var mapping = CreateDefault(playerIndex);
                foreach (KeyValuePair<string, string> pair in serialized)
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

            string json = JsonSerializer.Serialize(serialized, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(filePath, json);
        }
    }
}
