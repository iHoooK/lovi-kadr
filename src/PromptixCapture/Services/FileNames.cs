using System.Globalization;
using System.Text.RegularExpressions;

namespace PromptixCapture.Services;

public static class FileNames
{
    public static string Format(string template, DateTime time, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("Шаблон имени пуст.");
        var value = Regex.Replace(template, @"\{([^}]+)\}", m => m.Groups[1].Value switch
        {
            "width" => width.ToString(CultureInfo.InvariantCulture),
            "height" => height.ToString(CultureInfo.InvariantCulture),
            "counter" => "1",
            var format when Regex.IsMatch(format, @"^[yMdHhmsf\-_. ]+$") => time.ToString(format, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("Неизвестный токен имени: " + m.Value)
        });
        // Windows rules even when unit tests are run on Linux.
        value = Regex.Replace(value, "[<>:\"/\\\\|?*\\x00-\\x1F]", "_").Trim().TrimEnd('.');
        if (value.Length == 0 || value.Contains('{') || value.Contains('}')) throw new ArgumentException("Неверный шаблон имени.");
        if (Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase)) value = "_" + value;
        return value[..Math.Min(value.Length, 180)];
    }
    public static string Unique(string folder, string template, string extension, int width = 0, int height = 0)
    {
        Directory.CreateDirectory(folder);
        var stem = Format(template, DateTime.Now, width, height);
        var path = Path.Combine(folder, stem + extension);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{stem}-{i}{extension}");
        return path;
    }
}
