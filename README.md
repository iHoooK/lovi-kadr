# ЛовиКадр

Локальное приложение для Windows: снимки экрана, длинные скриншоты, аннотации и запись MP4-видео. Без аккаунтов, рекламы и облака.

## Установить готовую программу

На странице релиза GitHub скачайте `LoviKadr-Setup-x64.exe`, запустите его и следуйте шагам установщика. Это один файл, который можно отправить любому пользователю Windows 10 (22H2) / 11 x64.

Если запись видео не запускается, установите [Microsoft Visual C++ Redistributable x64](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). В остальном .NET пользователю не нужен: он уже включён в установщик.

Portable-вариант `LoviKadr-Portable` работает без установки, но его нельзя разбирать: переносите всю папку, а не только `LoviKadr.exe`.

## Собрать из исходного кода

Нужны только три инструмента:

1. [Git for Windows](https://git-scm.com/download/win) — в установщике оставьте пункт **Git Bash Here**.
2. [.NET SDK 10 для Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) — скачайте именно **SDK**, не Runtime.
3. [Inno Setup 6](https://jrsoftware.org/isdl.php) — создаёт готовый установщик `.exe`.

Скачайте исходники кнопкой **Code → Download ZIP** и распакуйте их либо выполните:

```bash
git clone git@github.com:iHoooK/lovi-kadr.git
cd lovi-kadr
```

Откройте Git Bash в папке проекта, закройте запущенную версию ЛовиКадра и выполните одну команду:

```bash
bash scripts/build-release.sh
```

При первом запуске команда скачает зависимости NuGet. После успешной сборки появится папка `release`:

| Файл / папка | Назначение |
| --- | --- |
| `release/LoviKadr-Setup-x64.exe` | Готовый установщик. Это рекомендуемый файл для распространения. |
| `release/LoviKadr-Portable/` | Версия без установки. Передавать только целиком. |

Папка `release` создаётся заново при каждой сборке и не попадает в GitHub. Старые служебные результаты сборки не публикуются.

## Приватность и лицензия

Политика обработки данных — [PRIVACY.md](PRIVACY.md). Код распространяется по [MIT License](LICENSE); перечень сторонних компонентов — [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
