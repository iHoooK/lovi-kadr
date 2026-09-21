using System.Windows.Input;
using System.Windows.Interop;
using PromptixCapture.Helpers;
using PromptixCapture.Models;

namespace PromptixCapture.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _actions = new();
    public event Action<string>? Pressed;
    public event Action? ShowSettings;
    public HotkeyService()
    {
        // Top-level invisible window receives broadcast messages from a second instance.
        _source = new HwndSource(new HwndSourceParameters("LoviKadr Commands") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
    }
    public static (uint Modifiers, uint Key) Parse(string value)
    {
        uint modifiers = 0;
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("Пустая комбинация.");
        foreach (var part in parts[..^1]) modifiers |= part.ToLowerInvariant() switch
        {
            "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u,
            _ => throw new ArgumentException("Неизвестный модификатор: " + part)
        };
        var name = parts[^1].Equals("PrtScr", StringComparison.OrdinalIgnoreCase) ? "PrintScreen" : parts[^1];
        if (!Enum.TryParse<Key>(name, true, out var key) || key is Key.None or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new ArgumentException("Неизвестная клавиша: " + name);
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    public List<string> Register(HotkeySettings settings)
    {
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        var errors = new List<string>();
        int next = 1;
        foreach (var property in typeof(HotkeySettings).GetProperties())
        {
            var text = property.GetValue(settings) as string;
            if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var (mod, key) = Parse(text);
                int id = next++;
                if (!NativeMethods.RegisterHotKey(_source.Handle, id, mod | 0x4000, key)) errors.Add(text + " — уже занято.");
                else _actions[id] = property.Name;
            }
            catch (ArgumentException ex) { errors.Add(text + ": " + ex.Message); }
        }
        return errors;
    }
    private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x312 && _actions.TryGetValue(w.ToInt32(), out var action)) { _source.Dispatcher.BeginInvoke(new Action(() => Pressed?.Invoke(action))); handled = true; }
        if ((uint)msg == NativeMethods.ShowSettingsMessage) { _source.Dispatcher.BeginInvoke(new Action(() => ShowSettings?.Invoke())); handled = true; }
        return IntPtr.Zero;
    }
    public void Dispose() { foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id); _source.Dispose(); }
}
