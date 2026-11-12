using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Чат с ИИ через OpenAI-совместимый API (chat/completions).</summary>
public partial class AiChatPanel : UserControl
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly List<(string role, string content)> _history = new();

    public AiChatPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var s = SettingsService.Current;
            ApiBaseUrlBox.Text = s.AiBaseUrl;
            ApiKeyBox.Password = s.AiApiKey;
            ApiModelBox.Text = s.AiModel;

            if (_history.Count == 0)
                AddBubble("Привет! Настройте API через кнопку «API» сверху (подойдёт любой OpenAI-совместимый сервис) — и спрашивайте.", isUser: false);
        };
    }

    private void ToggleApiSettings(object s, RoutedEventArgs e) =>
        ApiSettings.Visibility = ApiSettings.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void Input_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Send_Click(s, e);
        }
    }

    private async void Send_Click(object s, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // сохраняем настройки API
        var st = SettingsService.Current;
        st.AiBaseUrl = ApiBaseUrlBox.Text.Trim().TrimEnd('/');
        st.AiApiKey = ApiKeyBox.Password;
        st.AiModel = ApiModelBox.Text.Trim();
        SettingsService.Save();

        if (string.IsNullOrEmpty(st.AiApiKey))
        {
            AddBubble("Укажите API-ключ в настройках (кнопка «API» сверху).", isUser: false);
            return;
        }

        InputBox.Clear();
        AddBubble(text, isUser: true);
        _history.Add(("user", text));

        var thinking = AddBubble("Думаю…", isUser: false);
        SendButton.IsEnabled = false;

        try
        {
            var reply = await AskAsync(st, text);
            _history.Add(("assistant", reply));
            ChatPanel.Children.Remove(thinking);
            AddBubble(reply, isUser: false);
        }
        catch (Exception ex)
        {
            ChatPanel.Children.Remove(thinking);
            AddBubble($"Ошибка: {ex.Message}", isUser: false);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private async Task<string> AskAsync(Models.AppSettings st, string text)
    {
        var messages = new List<object> { new { role = "system", content = "Ты — полезный ассистент в приложении AURA. Отвечай кратко и по делу, по-русски." } };
        foreach (var (role, content) in _history.TakeLast(20))
            messages.Add(new { role, content });

        var payload = JsonSerializer.Serialize(new { model = st.AiModel, messages, stream = false });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{st.AiBaseUrl}/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", st.AiApiKey);

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}");

        using var doc = JsonDocument.Parse(body);
        var replyContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return replyContent ?? "(пустой ответ)";
    }

    private Border AddBubble(string text, bool isUser)
    {
        var bubble = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = isUser ? new Thickness(60, 3, 0, 3) : new Thickness(0, 3, 60, 3),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 460,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextPriBrush")
            }
        };
        if (isUser)
            bubble.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        else
            bubble.Background = (Brush)FindResource("BgCardBrush");

        ChatPanel.Children.Add(bubble);
        ChatScroll.ScrollToEnd();
        return bubble;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
