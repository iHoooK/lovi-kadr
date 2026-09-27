using Microsoft.Win32;

namespace PromptixCapture.Services;

public static class AutostartService
{
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("LoviKadr") is string value && !string.IsNullOrWhiteSpace(value);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (!enabled) { key.DeleteValue("LoviKadr", false); return; }
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь EXE.");
        if (!Path.GetFileName(exe).Equals("LoviKadr.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Не удалось включить автозапуск. Запустите ЛовиКадр из установленной программы или постоянной папки portable-версии и попробуйте снова.");
        key.SetValue("LoviKadr", $"\"{exe}\" --startup");
    }
}
