using System.Diagnostics;
using Microsoft.Win32;

namespace PromptixCapture.Services;

internal static class UpdateInstallService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D775F941-FA98-40AD-B96F-1417858DD72E}_is1";

    public static bool IsInstalled
    {
        get
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") is string folder &&
                string.Equals(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder)),
                    System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Launch(DownloadedInstaller installer)
    {
        if (!IsInstalled) throw new InvalidOperationException("Для portable-версии скачайте новый архив со страницы релиза.");
        var start = new ProcessStartInfo(installer.Path)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "/SP- /NORESTART /DIR=\"" + System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) + "\""
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить установщик.");
    }
}
