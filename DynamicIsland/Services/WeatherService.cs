using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace DynamicIsland.Services;

public class WeatherInfo
{
    public bool Available;
    public string City = "";
    public double Temp;
    public double FeelsLike;
    public double Wind;         // м/с
    public int Code;            // WMO weather code
    public string Error = "";
    public List<(string Time, double Temp, int Code)> Hours = new();
    public List<(string Day, double Min, double Max, int Code)> Daily = new();
}

/// <summary>
/// Погода: Open-Meteo (без ключа), локация — WinRT Geolocator с округлением до 0.01°,
/// запасной вариант — ip-api.com. Обновление раз в 15 минут.
/// </summary>
public static class WeatherService
{
    public static WeatherInfo Info { get; } = new();
    public static event Action? Updated;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static DispatcherTimer? _timer;
    private static bool _busy;
    private static (double Lat, double Lon, string City)? _location;

    public static void Init()
    {
        SettingsService.Changed += () =>
        {
            if (SettingsService.Current.ShowWeather) _ = RefreshAsync();
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public static async Task RefreshAsync()
    {
        if (_busy || !SettingsService.Current.ShowWeather) return;
        _busy = true;
        try
        {
            _location ??= await ResolveLocationAsync();
            if (_location is not { } loc)
            {
                Info.Available = false;
                Info.Error = "Включите геолокацию Windows или проверьте сеть";
                Updated?.Invoke();
                return;
            }

            string lat = loc.Lat.ToString("0.00", CultureInfo.InvariantCulture);
            string lon = loc.Lon.ToString("0.00", CultureInfo.InvariantCulture);
            var url = "https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={lat}&longitude={lon}" +
                      "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m" +
                      "&hourly=temperature_2m,weather_code&forecast_hours=5" +
                      "&daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days=7" +
                      "&wind_speed_unit=ms&timezone=auto";

            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            Info.Temp = cur.GetProperty("temperature_2m").GetDouble();
            Info.FeelsLike = cur.GetProperty("apparent_temperature").GetDouble();
            Info.Wind = cur.GetProperty("wind_speed_10m").GetDouble();
            Info.Code = cur.GetProperty("weather_code").GetInt32();
            Info.City = loc.City;

            Info.Hours.Clear();
            var hourly = doc.RootElement.GetProperty("hourly");
            var times = hourly.GetProperty("time").EnumerateArray().ToArray();
            var temps = hourly.GetProperty("temperature_2m").EnumerateArray().ToArray();
            var codes = hourly.GetProperty("weather_code").EnumerateArray().ToArray();
            for (int i = 0; i < times.Length && i < 5; i++)
                Info.Hours.Add((times[i].GetString()?[11..16] ?? "—",
                    temps[i].GetDouble(), codes[i].GetInt32()));

            Info.Daily.Clear();
            var daily = doc.RootElement.GetProperty("daily");
            var days = daily.GetProperty("time").EnumerateArray().ToArray();
            var maxs = daily.GetProperty("temperature_2m_max").EnumerateArray().ToArray();
            var mins = daily.GetProperty("temperature_2m_min").EnumerateArray().ToArray();
            var dcodes = daily.GetProperty("weather_code").EnumerateArray().ToArray();
            var ru = new CultureInfo("ru-RU");
            for (int i = 0; i < days.Length && i < 7; i++)
            {
                string day = DateTime.TryParse(days[i].GetString(), out var dt)
                    ? (i == 0 ? "Сегодня" : dt.ToString("ddd", ru))
                    : "—";
                Info.Daily.Add((day, mins[i].GetDouble(), maxs[i].GetDouble(), dcodes[i].GetInt32()));
            }

            Info.Available = true;
            Info.Error = "";
        }
        catch
        {
            Info.Available = false;
            Info.Error = "Не удалось получить прогноз";
        }
        finally
        {
            _busy = false;
            Updated?.Invoke();
        }
    }

    /// <summary>Локация: сначала WinRT-геолокатор, затем ip-api.com по IP.</summary>
    private static async Task<(double, double, string)?> ResolveLocationAsync()
    {
        try
        {
            var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
            if (access == Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
            {
                var geo = new Windows.Devices.Geolocation.Geolocator
                {
                    DesiredAccuracyInMeters = 3000
                };
                var pos = await geo.GetGeopositionAsync();
                double lat = Math.Round(pos.Coordinate.Point.Position.Latitude, 2);
                double lon = Math.Round(pos.Coordinate.Point.Position.Longitude, 2);
                return (lat, lon, "");
            }
        }
        catch { /* геолокация отключена — пробуем по IP */ }

        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("http://ip-api.com/json/?fields=status,lat,lon,city"));
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() == "success")
            {
                return (Math.Round(root.GetProperty("lat").GetDouble(), 2),
                        Math.Round(root.GetProperty("lon").GetDouble(), 2),
                        root.GetProperty("city").GetString() ?? "");
            }
        }
        catch { }
        return null;
    }

    /// <summary>WMO-код → иконка и описание.</summary>
    public static (string Icon, string Text) Describe(int code) => code switch
    {
        0 => ("ic_sun", "Ясно"),
        1 or 2 => ("ic_cloud_sun", "Переменная облачность"),
        3 => ("ic_cloud", "Пасмурно"),
        45 or 48 => ("ic_cloud_fog", "Туман"),
        >= 51 and <= 57 => ("ic_cloud_rain", "Морось"),
        >= 61 and <= 67 or >= 80 and <= 82 => ("ic_cloud_rain", "Дождь"),
        >= 71 and <= 77 or 85 or 86 => ("ic_snowflake", "Снег"),
        >= 95 => ("ic_cloud_lightning", "Гроза"),
        _ => ("ic_cloud_sun", "Облачно"),
    };
}
