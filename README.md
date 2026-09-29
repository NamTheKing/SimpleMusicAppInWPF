# Simple Music App in WPF

A small music player built with WPF (.NET 8) — code-behind only, easy to read while learning WPF.

## Features
- Add songs via **＋ Add songs** or drag & drop files onto the window (mp3, wav, wma, m4a, aac)
- Double-click / Enter to play, Delete to remove from playlist
- Play / Pause, Stop, Previous, Next (auto-plays the next song, wraps around)
- Seek bar (click or drag) with current / total time
- Volume slider

## Run
Requires Windows + .NET 8 SDK.
```
cd SimpleMusicApp
dotnet run
```
Or open `SimpleMusicApp/SimpleMusicApp.csproj` in Visual Studio and press F5.

## Project layout
| File | What it does |
|---|---|
| `App.xaml` | Shared colors and the round button style |
| `MainWindow.xaml` | UI layout (playlist, progress bar, buttons, volume) |
| `MainWindow.xaml.cs` | Player logic: `MediaPlayer` + `DispatcherTimer` |
| `Models/Track.cs` | One playlist entry (file path + title) |
