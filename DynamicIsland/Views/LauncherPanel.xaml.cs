using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Models;
using DynamicIsland.Services;
using Microsoft.VisualBasic;
using Microsoft.Win32;

namespace DynamicIsland.Views;

/// <summary>Лаунчер пользовательских приложений с категориями.</summary>
public partial class LauncherPanel : UserControl
{
    private string _currentCategory = "Все";

    public LauncherPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        BuildCategories();
        BuildApps();
    }

    // ---------- Категории ----------

    private void BuildCategories()
    {
        CategoryRail.Items.Clear();
        var cats = new List<string> { "Все" };
        cats.AddRange(SettingsService.Current.AppCategories);

        foreach (var cat in cats)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("ChipButton"),
                Content = cat,
                Tag = cat,
                Margin = new Thickness(0, 0, 6, 0)
            };
            if (cat == _currentCategory)
            {
                btn.Background = (Brush)FindResource("AccentSoftBrush");
                btn.BorderBrush = (Brush)FindResource("AccentBrush");
            }
            btn.Click += (_, _) => { _currentCategory = cat; Refresh(); };
            CategoryRail.Items.Add(btn);
        }
    }

    private void AddCategory_Click(object s, RoutedEventArgs e)
    {
        var name = Interaction.InputBox("Название новой категории:", "Категория", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!SettingsService.Current.AppCategories.Contains(name))
        {
            SettingsService.Current.AppCategories.Add(name);
            SettingsService.Save();
        }
        _currentCategory = name;
        Refresh();
    }

    // ---------- Приложения ----------

    private void BuildApps()
    {
        AppsWrap.Children.Clear();
        var apps = SettingsService.Current.Apps
            .Where(a => _currentCategory == "Все" || a.Category == _currentCategory);

        foreach (var app in apps)
            AppsWrap.Children.Add(BuildAppCard(app));

        if (!AppsWrap.Children.Cast<UIElement>().Any())
        {
            AppsWrap.Children.Add(new TextBlock
            {
                Text = "Пока пусто. Нажмите «＋ Приложение», чтобы добавить ярлык.",
                Foreground = (Brush)FindResource("TextSecBrush"),
                FontSize = 12, Margin = new Thickness(4, 12, 0, 0)
            });
        }
    }

    private UIElement BuildAppCard(AppShortcut app)
    {
        ImageSource? icon = File.Exists(app.Path) ? IconHelper.GetFileIcon(app.Path) : null;

        var iconEl = icon != null
            ? (FrameworkElement)new Image { Source = icon, Width = 34, Height = 34 }
            : new System.Windows.Shapes.Path
            {
                Style = (Style)FindResource("VecIcon"),
                Width = 30, Height = 30,
                Data = (Geometry)Application.Current.FindResource("ic_package"),
                Stroke = (Brush)FindResource("TextSecBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };

        var card = new Button
        {
            Style = (Style)FindResource("CardButton"),
            Width = 96, Height = 92, Margin = new Thickness(4),
            ToolTip = app.Path,
            Content = new StackPanel
            {
                Children =
                {
                    iconEl,
                    new TextBlock
                    {
                        Text = app.Name, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
                    }
                }
            }
        };
        card.Click += (_, _) => SystemControlService.Launch(app.Path);

        var menu = new ContextMenu();
        var del = new MenuItem { Header = "Удалить" };
        del.Click += (_, _) =>
        {
            SettingsService.Current.Apps.Remove(app);
            SettingsService.Save();
            Refresh();
        };
        menu.Items.Add(del);
        card.ContextMenu = menu;

        return card;
    }

    private void AddApp_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Программы и ярлыки|*.exe;*.lnk;*.bat;*.cmd|Все файлы|*.*",
            Title = "Выберите приложение"
        };
        if (dlg.ShowDialog() != true) return;

        var name = Interaction.InputBox("Имя ярлыка:", "Приложение",
            Path.GetFileNameWithoutExtension(dlg.FileName));
        if (string.IsNullOrWhiteSpace(name)) return;

        var category = _currentCategory == "Все" ? "Общее" : _currentCategory;
        if (!SettingsService.Current.AppCategories.Contains(category))
            SettingsService.Current.AppCategories.Add(category);

        SettingsService.Current.Apps.Add(new AppShortcut
        {
            Name = name,
            Path = dlg.FileName,
            Category = category
        });
        SettingsService.Save();
        Refresh();
    }
}
