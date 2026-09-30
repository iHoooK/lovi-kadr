using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using PromptixCapture.Services;

namespace PromptixCapture.Windows;

public sealed partial class SettingsWindow
{
    private readonly Func<DownloadedInstaller, bool>? _installUpdate;
    private StackPanel? _updateSection;
    private TextBlock _updateStatus = null!;
    private Button _checkUpdate = null!;
    private Button _downloadUpdate = null!;
    private Button _cancelUpdate = null!;
    private ProgressBar _updateProgress = null!;
    private CancellationTokenSource? _updateCancellation;
    private UpdateRelease? _availableUpdate;
    private bool _updateBusy;
    private bool _updatesClosed;
    private bool _installedCopy;

    private void BuildUpdateSection()
    {
        if (_updateSection is null)
        {
            _updateSection = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
            _updateSection.Children.Add(Ui.Text("Обновления", 16));
            _updateSection.Children.Add(Ui.Text("Проверка выполняется только по кнопке. Программа обращается к GitHub; снимки, видео и настройки не передаются.", 13, true));
            _updateStatus = Ui.Text("Нажмите «Проверить обновления», чтобы узнать о новой версии.", 13, true);
            _updateSection.Children.Add(_updateStatus);
            _updateProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8,
                Margin = new Thickness(3, 4, 3, 8), Visibility = Visibility.Collapsed };
            _updateSection.Children.Add(_updateProgress);
            _checkUpdate = Ui.Button("Проверить обновления", () => _ = CheckUpdateAsync());
            _downloadUpdate = Ui.Button("Скачать и установить…", () => _ = DownloadUpdateAsync());
            _downloadUpdate.Visibility = Visibility.Collapsed;
            _cancelUpdate = Ui.Button("Отменить", () => _updateCancellation?.Cancel());
            _cancelUpdate.Visibility = Visibility.Collapsed;
            _updateSection.Children.Add(_checkUpdate);
            _updateSection.Children.Add(_downloadUpdate);
            _updateSection.Children.Add(_cancelUpdate);
            _updateSection.Children.Add(Ui.Button("Открыть страницу релизов ↗",
                () => Ui.Open((_availableUpdate?.Page.ToString()) ?? UpdateService.ReleasesPage)));
        }
        _page.Children.Add(_updateSection);
    }

    private void SetUpdateBusy(bool busy, bool downloading = false)
    {
        _updateBusy = busy;
        _checkUpdate.IsEnabled = !busy;
        _downloadUpdate.IsEnabled = !busy;
        _cancelUpdate.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _updateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _updateProgress.IsIndeterminate = !downloading;
        _updateProgress.Value = 0;
    }

    private async Task CheckUpdateAsync()
    {
        if (_updateBusy || _updatesClosed) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true);
        _availableUpdate = null;
        _downloadUpdate.Visibility = Visibility.Collapsed;
        _updateStatus.Text = "Проверяем последнюю версию на GitHub…";
        try
        {
            using var service = new UpdateService();
            var release = await service.CheckAsync(Version.Parse(App.DisplayVersion), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _availableUpdate = release;
            if (release is null) { _updateStatus.Text = "У вас установлена актуальная версия."; return; }
            _installedCopy = UpdateInstallService.IsInstalled && _installUpdate is not null;
            _updateStatus.Text = $"Доступна версия {release.Version}. " +
                (!_installedCopy ? "Для portable-версии скачайте новый архив со страницы релиза, закройте программу и распакуйте архив в новую папку." :
                 !release.CanInstall ? "В релизе пока нет установщика с контрольной суммой SHA-256. Откройте страницу релиза или проверьте позже." :
                 $"Установщик: {release.Size / 1024d / 1024:F1} МБ. Настройки и ваши файлы сохранятся.");
            if (_installedCopy && release.CanInstall) _downloadUpdate.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { _updateStatus.Text = cancellation.IsCancellationRequested ? "Проверка отменена." : "GitHub не ответил вовремя. Попробуйте снова."; }
        catch (Exception ex) { ReportUpdateError(ex); }
        finally { _updateCancellation = null; SetUpdateBusy(false); }
    }

    private async Task DownloadUpdateAsync()
    {
        if (_updateBusy || _updatesClosed || _availableUpdate is not { CanInstall: true } release || !_installedCopy) return;
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetUpdateBusy(true, true);
        _updateStatus.Text = "Скачиваем установщик…";
        DownloadedInstaller? installer = null;
        bool launched = false;
        bool downloading = true;
        try
        {
            using var service = new UpdateService();
            var progress = new Progress<int>(percent =>
            {
                if (_updatesClosed || cancellation.IsCancellationRequested || !_updateBusy || !downloading) return;
                _updateProgress.Value = percent;
                _updateStatus.Text = percent == 100 ? "Проверяем SHA-256 установщика…" : $"Скачиваем установщик: {percent}%";
            });
            installer = await service.DownloadAsync(release, Path.Combine(LocalData.Folder, "Updates"), progress, cancellation.Token);
            downloading = false;
            cancellation.Token.ThrowIfCancellationRequested();
            _updateStatus.Text = "Установщик скачан. Проверка SHA-256 пройдена.";
            _cancelUpdate.Visibility = Visibility.Collapsed;
            _updateProgress.Visibility = Visibility.Collapsed;
            if (MessageBox.Show(this,
                $"Установить ЛовиКадр {release.Version}?\n\nПрограмма закроется и откроет мастер установки. Windows запросит права администратора. Сначала сохраните изменения настроек и завершите работу в редакторе.\n\nНастройки, снимки и видео сохранятся.",
                "Обновление ЛовиКадра", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                launched = _installUpdate?.Invoke(installer) == true;
            }
            if (!launched) _updateStatus.Text = "Установка отложена. Для новой попытки нажмите «Скачать и установить…».";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { _updateStatus.Text = "Запуск установщика отменён. Программа продолжает работать."; }
        catch (OperationCanceledException) { _updateStatus.Text = cancellation.IsCancellationRequested ? "Скачивание отменено." : "Время скачивания истекло. Попробуйте снова."; }
        catch (Exception ex) { ReportUpdateError(ex); }
        finally
        {
            downloading = false;
            if (installer is not null)
            {
                installer.Dispose();
                if (!launched)
                {
                    try { installer.Delete(); }
                    catch (IOException ex) { AppLog.Error("Delete unused update", ex); }
                    catch (UnauthorizedAccessException ex) { AppLog.Error("Delete unused update", ex); }
                }
            }
            _updateCancellation = null;
            SetUpdateBusy(false);
        }
    }

    private void ReportUpdateError(Exception ex)
    {
        AppLog.Error("Update", ex);
        _updateStatus.Text = ex is HttpRequestException
            ? "Не удалось связаться с GitHub. Проверьте интернет и попробуйте снова."
            : ex is System.Text.Json.JsonException or KeyNotFoundException or FormatException
                ? "GitHub вернул некорректные сведения о релизе. Попробуйте позже."
                : "Обновление не выполнено: " + ex.Message;
    }
}
