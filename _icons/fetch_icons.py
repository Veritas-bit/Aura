"""Скачивает Phosphor Icons (fill-вариант, стиль SF regular filled) и конвертирует
их SVG в StreamGeometry для WPF. Ключи ресурсов сохранены прежними (ic_*),
поэтому вся разметка продолжает работать без переименований."""
import re
import urllib.request
import xml.etree.ElementTree as ET

# ключ ресурса -> имя иконки phosphor (fill)
ICONS = {
    # навигация
    "house": "house", "music": "music-note", "rocket": "rocket", "bot": "robot",
    "radio_tower": "broadcast", "sliders_horizontal": "faders-horizontal",
    "puzzle": "puzzle-piece", "settings": "gear-six",
    # батарея
    "battery_full": "battery-full", "battery_medium": "battery-medium",
    "battery_low": "battery-low", "battery_charging": "battery-charging",
    # система
    "wifi": "wifi-high", "bluetooth": "bluetooth", "shield": "shield-check",
    "moon": "moon", "sun": "sun", "power": "power",
    "rotate_ccw": "arrow-counter-clockwise", "log_out": "sign-out",
    "clipboard_list": "clipboard-text", "volume_2": "speaker-high",
    "volume_x": "speaker-slash",
    # главная
    "circle_dot": "record", "camera": "camera", "notebook_pen": "note-pencil",
    "alarm_clock": "alarm", "calculator": "calculator", "timer": "timer",
    "clipboard": "clipboard", "folder": "folder",
    # плеер
    "play": "play", "pause": "pause", "skip_back": "skip-back",
    "skip_forward": "skip-forward", "shuffle": "shuffle", "repeat": "repeat",
    # прочее
    "minus": "minus", "x": "x", "send": "paper-plane-right", "plus": "plus",
    "check": "check", "package": "package", "zap": "lightning",
    "chevron_down": "caret-down", "trash_2": "trash", "cpu": "cpu",
    "memory_stick": "memory", "hard_drive": "hard-drive", "gauge": "gauge",
    # widget store
    "cloud_sun": "cloud-sun", "coins": "coins", "gamepad_2": "game-controller",
    "newspaper": "newspaper", "headphones": "headphones", "chart_line": "chart-line",
    "bitcoin": "currency-btc", "calendar_check": "calendar-check",
    # окно настроек и плитки
    "smile": "smiley", "keyboard": "keyboard", "download": "download-simple",
    "lock": "lock", "palette": "palette",
}

NS = "{http://www.w3.org/2000/svg}"
NUM = r"[-+]?(?:[0-9]*\.[0-9]+|[0-9]+)(?:[eE][-+]?[0-9]+)?"


def fetch(name):
    url = f"https://cdn.jsdelivr.net/npm/@phosphor-icons/core@latest/assets/fill/{name}-fill.svg"
    with urllib.request.urlopen(url, timeout=30) as r:
        return r.read().decode("utf-8")


def path_d(d):
    """Чистит d и чинит ведущий относительный moveto при склейке подпутей."""
    d = d.strip()
    m = re.match(rf"^\s*m\s*({NUM})[\s,]*({NUM})[\s,]*", d)
    if m:
        rest = d[m.end():]
        d = f"M{m.group(1)} {m.group(2)}" + (" l" + rest if rest else "")
    return d


def main():
    out = ['<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
           '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
           '    <!-- Phosphor Icons (MIT) — fill-вариант в духе SF Symbols regular filled.',
           '         Заливаются через Fill (кисти TextPriBrush/AccentBrush/...). -->']
    fails = 0
    for key, name in ICONS.items():
        try:
            root = ET.fromstring(fetch(name))
            parts = [path_d(el.attrib.get("d", "")) for el in root.iter(NS + "path")]
            parts = [p for p in parts if p]
            if not parts:
                raise ValueError("no paths")
            out.append(f'    <StreamGeometry x:Key="ic_{key}" FillRule="Nonzero">{" ".join(parts)}</StreamGeometry>')
            print(f"ok   {key:20s} <- {name}-fill")
        except Exception as ex:
            fails += 1
            print(f"FAIL {key:20s} <- {name}: {ex}")
    out.append("</ResourceDictionary>")
    if fails == 0:
        with open("../DynamicIsland/Resources/Icons.xaml", "w", encoding="utf-8") as f:
            f.write("\n".join(out) + "\n")
        print("saved Icons.xaml")
    else:
        print(f"{fails} ошибок — файл НЕ перезаписан")


if __name__ == "__main__":
    main()
