# Simple Music App in WPF

A Spotify-style music player built with WPF (.NET 8) and [NAudio](https://github.com/naudio/NAudio), with a live audio visualizer.

## Features
- Spotify-like dark UI: library panel, "Now playing" header, bottom player bar
- **Visualizer**: 48 bars that move with the music's loudness per frequency (bass → treble); the cover art pulses with the bass
- Add songs with **+** or drag & drop (mp3, wav, wma, m4a, aac, flac)
- Double-click / Enter to play, Delete to remove, **Space** to play/pause
- Previous / Next (auto-plays the next song), seek bar, volume

## Run
Requires Windows 10/11 + .NET 8 SDK.
```
cd SimpleMusicApp
dotnet run
```

## Project layout
| File | What it does |
|---|---|
| `App.xaml` | Colors + styles (icon buttons, round play button, thin slider, playlist rows) |
| `MainWindow.xaml` | Layout |
| `MainWindow.xaml.cs` | UI logic, timer, visualizer animation |
| `Audio/AudioPlayer.cs` | NAudio wrapper: play/pause/seek/volume + FFT spectrum |
| `Audio/SampleCapture.cs` | Copies the samples flowing to the speakers so they can be analysed |
| `Models/Track.cs` | One playlist entry |
