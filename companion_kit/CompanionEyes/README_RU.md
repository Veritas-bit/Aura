# Companion Eyes — прозрачный WPF-компаньон для Windows Dynamic Island

Готовая демонстрационная заготовка на **C# / WPF / .NET 8**. Контрол рисует глаза на прозрачном фоне и не создаёт собственную подложку островка.

## Состав комплекта

- `Assets/eyes-atlas.png` — основной RGBA-атлас 4×4, 16 поз, ячейка 384×256.
- `Assets/Frames/pose_00.png` … `pose_15.png` — отдельные PNG-кадры из атласа.
- `Assets/two-eyes-preview.png` — парный предварительный просмотр.
- `Assets/Layers/eye_left.png`, `eye_right.png` — отдельные PNG-глаза, вырезанные из парного превью.
- `Controls/CompanionEyesControl.cs` — контрол поведения и отрисовки.
- `MainWindow.*` — демонстрационное прозрачное окно с тестовыми переключателями.

> Важно: это комплект **PNG-слоёв/кадров для интеграции**, но не полностью разложенная PSD-подобная анатомия глаза. Зрачок, радужка и блики пока нарисованы вместе внутри каждого кадра. Для абсолютно плавного независимого движения зрачков понадобится отдельная согласованная генерация sclera / iris / pupil / highlights с одинаковой геометрией и масками.

## Запуск

Нужен Windows и .NET 8 SDK с компонентом Desktop development / WPF.

```powershell
dotnet restore
dotnet run
```

## Интеграция в существующий проект

1. Скопируйте `Controls/CompanionEyesControl.cs` и папку `Assets`.
2. В `.csproj` добавьте ресурс:

```xml
<ItemGroup>
  <Resource Include="Assets\eyes-atlas.png" />
</ItemGroup>
```

3. Добавьте namespace и контрол в существующий XAML островка:

```xml
xmlns:eyes="clr-namespace:CompanionEyes.Controls"

<eyes:CompanionEyesControl x:Name="MascotEyes"
    Width="430" Height="285"
    HorizontalAlignment="Center"
    VerticalAlignment="Center" />
```

4. Окно верхнего уровня должно поддерживать прозрачность:

```xml
<Window AllowsTransparency="True" Background="Transparent" WindowStyle="None">
```

Если у тебя уже есть окно Dynamic Island с `AllowsTransparency=True`, второй Window создавать не нужно — просто добавь контрол в существующий Grid. Сам контрол `IsHitTestVisible=False`, поэтому не перехватывает клики.

## API управления

```csharp
MascotEyes.TrackPointer(Mouse.GetPosition(MascotEyes));
MascotEyes.PointerLeft();
MascotEyes.SetMode(CompanionMode.Searching);
MascotEyes.SetMode(CompanionMode.Sleeping);
MascotEyes.NotifyAttention();
MascotEyes.SetTyping(true);       // false — набор закончен
MascotEyes.SetEmotion(CompanionEmotion.Happy);
MascotEyes.SetVoiceLevel(rms);    // уровень громкости 0..1
MascotEyes.Wake();
MascotEyes.Dispose();             // при закрытии окна
```

Режимы: `Idle`, `Tracking`, `Searching`, `Sleeping`, `Attention`, `Typing`, `Voice`.

Эмоции: `Neutral`, `Happy`, `Curious`, `Surprised`, `Shy`, `Annoyed`, `Worried`.

### Подключение курсора

В обработчике `MouseMove` родительского окна:

```csharp
MascotEyes.TrackPointer(e.GetPosition(MascotEyes));
```

В `MouseLeave`:

```csharp
MascotEyes.PointerLeft();
```

### Подключение набора текста

Если приложение получает события ввода или знает, что пользователь печатает:

```csharp
MascotEyes.SetTyping(true);
// по окончании ввода:
MascotEyes.SetTyping(false);
```

### Подключение голоса

`SetVoiceLevel` не записывает микрофон сам. Передавай туда RMS/амплитуду уже полученного аудиобуфера (0..1) примерно 15–30 раз в секунду. Для TTS передавай уровень проигрываемого аудио. Не отправляй аудио в сеть ради анимации глаз.

Пример простого RMS для `float[] samples`:

```csharp
double sum = 0;
foreach (float sample in samples) sum += sample * sample;
double rms = Math.Sqrt(sum / Math.Max(1, samples.Length));
MascotEyes.SetVoiceLevel(Math.Clamp(rms * 3.5, 0, 1));
```

## Примечания

- Прозрачность хранится в alpha-канале PNG. Не добавляй `Background` к самому контролу.
- Контрол использует один authored eye и зеркалит его для второй стороны — это сохраняет симметрию и экономит ресурсы.
- Слежение сейчас переключает между нарисованными позами атласа, а не двигает зрачок отдельным слоем.
- Для встраивания в текущий проект Dynamic Island используй только `CompanionEyesControl.cs` и `Assets/eyes-atlas.png`; `MainWindow.*` — самостоятельная демонстрация.
