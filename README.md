# Cinematic Search — Liquid Glass WebView2 UI

Обновлённая версия приложения поиска с современным интерфейсом:

- WinForms оболочка;
- WebView2 frontend;
- отдельные `HTML / CSS / JS` файлы;
- cinematic Liquid Glass дизайн;
- floating panels;
- adaptive cards;
- тёмная и светлая тема;
- taskbar-safe кастомное окно;
- сохранена логика поиска файлов и папок по ключевым словам;
- сохранение результатов в `.txt` отчёт.

## Запуск

1. Открой `search.sln` в Visual Studio.
2. Установи workload `.NET desktop development`.
3. Убедись, что установлен WebView2 Runtime.
4. Нажми `F5`.

## Структура

```text
search/
├── Form1.cs          # WinForms + WebView2 backend
├── Program.cs
├── search.csproj
└── Web/
    ├── index.html    # интерфейс
    ├── styles.css    # Liquid Glass UI
    └── app.js        # логика frontend и bridge с C#
```

## Fixed WebView2 Runtime

Для portable-распространения можно положить Fixed Runtime в:

```text
search/bin/Release/net8.0-windows/Runtime/WebView2
```

Если рядом с приложением будет `Runtime/WebView2/msedgewebview2.exe`, приложение автоматически использует этот runtime.
