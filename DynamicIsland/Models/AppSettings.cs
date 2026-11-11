namespace DynamicIsland.Models;

public class AppShortcut
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Category { get; set; } = "Общее";
}

public class StreamAction
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "ic:zap";
    public string Command { get; set; } = "";
}

public class AppSettings
{
    // Внешний вид
    public string EyeColor { get; set; } = "#7C4DFF";
    public string Theme { get; set; } = "custom"; // custom | classic | system | morning | day | evening | night | auto
    public string CollapsedStyle { get; set; } = "Island"; // Island | Notch
    public bool GlowEnabled { get; set; } = true;
    public double UiOpacity { get; set; } = 0.96;

    // Содержимое свёрнутого вида
    public bool ShowClock { get; set; } = true;
    public bool ShowSeconds { get; set; } = false;
    public bool ShowBattery { get; set; } = true;
    public bool ShowStats { get; set; } = true;
    public bool StatCpu { get; set; } = true;
    public bool StatRam { get; set; } = true;
    public bool StatGpu { get; set; } = true;

    // Игровой режим: в полноэкранных играх — тонкий вырез с температурами, пингом и потерями
    public bool GameMode { get; set; } = true;

    // Погода на главной панели
    public bool ShowWeather { get; set; } = true;

    // Поведение
    public bool Topmost { get; set; } = true;
    public bool CollapseOnFocusLoss { get; set; } = false;
    public bool RunAtStartup { get; set; } = false;
    public string Hotkey { get; set; } = "Ctrl+Shift+Space";
    public bool CheckUpdates { get; set; } = true;

    // ИИ-чат (OpenAI-совместимый API)
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string AiApiKey { get; set; } = "";
    public string AiModel { get; set; } = "gpt-4o-mini";

    // Лаунчер
    public List<AppShortcut> Apps { get; set; } = new();
    public List<string> AppCategories { get; set; } = new() { "Общее", "Работа", "Игры", "Медиа" };

    // Плеер
    public List<string> Playlist { get; set; } = new();
    public double PlayerVolume { get; set; } = 0.7;

    // Панель стрима — иконки "ic:ключ" рисуются вектором, остальное — как текст (эмодзи)
    public List<StreamAction> StreamActions { get; set; } = new()
    {
        new() { Name = "OBS Studio", Icon = "ic:radio_tower", Command = "obs64" },
        new() { Name = "YouTube Studio", Icon = "ic:play", Command = "https://studio.youtube.com" },
        new() { Name = "Twitch Dashboard", Icon = "ic:zap", Command = "https://dashboard.twitch.tv" },
        new() { Name = "Discord", Icon = "ic:send", Command = "https://discord.com/app" },
        new() { Name = "VS Code", Icon = "ic:cpu", Command = "code" },
    };

    // Widget Store — отметки «жду модуль»
    public List<string> WidgetWishes { get; set; } = new();
}
