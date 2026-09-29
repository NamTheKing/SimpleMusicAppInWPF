using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SimpleMusicApp.Audio;
using SimpleMusicApp.Models;
using IOPath = System.IO.Path;                    // avoid clash with System.Windows.Shapes.Path
using Rectangle = System.Windows.Shapes.Rectangle;
using Track = SimpleMusicApp.Models.Track;       // avoid clash with System.Windows.Controls.Primitives.Track

namespace SimpleMusicApp;

public partial class MainWindow : Window
{
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac" };
    private const int BarCount = 48;

    private readonly AudioPlayer _player = new();
    private readonly ObservableCollection<Track> _playlist = new();

    // One timer (~33 fps) drives both the progress bar and the visualizer animation.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    // Visualizer
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly float[] _targetLevels = new float[BarCount];  // what the music says right now
    private readonly double[] _shownLevels = new double[BarCount]; // what we draw (smoothed)

    private int _currentIndex = -1;
    private bool _isDraggingSlider;
    private bool _isUpdatingSlider;

    public MainWindow()
    {
        InitializeComponent();

        PlaylistBox.ItemsSource = _playlist;
        _playlist.CollectionChanged += (_, _) =>
            EmptyHint.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _player.Volume = (float)VolumeSlider.Value;
        _player.TrackEnded += (_, _) => PlayNext();

        CreateVisualizerBars();
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e) => _player.Dispose();

    // ───────────── Playlist ─────────────

    private void AddSongs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose songs",
            Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
            AddFiles(dialog.FileNames);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            AddFiles(files.Where(f => AudioExtensions.Contains(IOPath.GetExtension(f).ToLowerInvariant())));
    }

    private void AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
            _playlist.Add(new Track(file));

        if (PlaylistBox.SelectedIndex < 0 && _playlist.Count > 0)
            PlaylistBox.SelectedIndex = 0;
    }

    private void PlaylistBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistBox.SelectedIndex >= 0)
            PlayTrack(PlaylistBox.SelectedIndex);
    }

    private void PlaylistBox_KeyDown(object sender, KeyEventArgs e)
    {
        int index = PlaylistBox.SelectedIndex;
        if (index < 0) return;

        if (e.Key == Key.Enter)
        {
            PlayTrack(index);
        }
        else if (e.Key == Key.Delete)
        {
            _playlist.RemoveAt(index);

            if (index == _currentIndex) StopAll();            // removed the playing song
            else if (index < _currentIndex) _currentIndex--;  // keep pointing at the same song
        }
    }

    // Space = play/pause anywhere in the window.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            TogglePlayPause();
            e.Handled = true; // stop the focused button from also "clicking"
        }
    }

    // ───────────── Playback ─────────────

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;

        var track = _playlist[index];
        try
        {
            _player.Open(track.FilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot play this file:\n{ex.Message}", "Error");
            return;
        }
        _player.Play();

        // Update which song is green in the list.
        if (_currentIndex >= 0 && _currentIndex < _playlist.Count)
            _playlist[_currentIndex].IsPlaying = false;
        track.IsPlaying = true;
        _currentIndex = index;

        PlaylistBox.SelectedIndex = index;
        BigTitleText.Text = track.Title;
        SmallTitleText.Text = track.Title;
        BigSubText.Text = $"Local file • {Format(_player.Duration)}";
        TotalTimeText.Text = Format(_player.Duration);
        CurrentTimeText.Text = "0:00";

        // Silently, otherwise shrinking Maximum would clamp Value → ValueChanged → seek the new song!
        _isUpdatingSlider = true;
        ProgressSlider.Value = 0;
        ProgressSlider.Maximum = Math.Max(1, _player.Duration.TotalSeconds);
        _isUpdatingSlider = false;
        SetPlayIcon(isPlaying: true);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void TogglePlayPause()
    {
        if (!_player.IsLoaded)
        {
            PlayTrack(Math.Max(PlaylistBox.SelectedIndex, 0)); // nothing loaded yet → start a song
            return;
        }

        if (_player.IsPlaying) _player.Pause();
        else _player.Play();

        SetPlayIcon(_player.IsPlaying);
    }

    private void StopAll()
    {
        _player.Close();
        _currentIndex = -1;
        SetPlayIcon(isPlaying: false);
        BigTitleText.Text = "No song selected";
        BigSubText.Text = "Pick a song from your library";
        SmallTitleText.Text = "—";
        SetSliderSilently(0);
        CurrentTimeText.Text = TotalTimeText.Text = "0:00";
    }

    private void Next_Click(object sender, RoutedEventArgs e) => PlayNext();

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;

        // Like Spotify: more than 3 s in → restart the song, otherwise go to the previous one.
        if (_player.Position.TotalSeconds > 3)
            _player.Position = TimeSpan.Zero;
        else
            PlayTrack(_currentIndex <= 0 ? _playlist.Count - 1 : _currentIndex - 1);
    }

    private void PlayNext()
    {
        if (_playlist.Count == 0) return;
        PlayTrack((_currentIndex + 1) % _playlist.Count); // wraps around to the first song
    }

    private void SetPlayIcon(bool isPlaying) =>
        PlayPauseButton.Content = isPlaying ? "" : ""; // Pause : Play glyph

    // ───────────── Timer: progress + visualizer ─────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_player.IsLoaded && !_isDraggingSlider)
        {
            SetSliderSilently(_player.Position.TotalSeconds);
            CurrentTimeText.Text = Format(_player.Position);
        }

        UpdateVisualizer();
    }

    private void CreateVisualizerBars()
    {
        // Each bar: green gradient, rounded, centered vertically → grows up AND down like a wave.
        var fill = new LinearGradientBrush(
            Color.FromRgb(0x1E, 0xD7, 0x60), Color.FromRgb(0x1D, 0x6F, 0xD9), 90);
        fill.Freeze(); // frozen brushes are faster to draw

        for (int i = 0; i < BarCount; i++)
        {
            _bars[i] = new Rectangle
            {
                Fill = fill,
                RadiusX = 3,
                RadiusY = 3,
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Height = 4
            };
            VisualizerGrid.Children.Add(_bars[i]);
        }
    }

    private void UpdateVisualizer()
    {
        _player.GetSpectrum(_targetLevels); // all zeros when paused → bars fall down
        double maxHeight = VisualizerGrid.ActualHeight;

        for (int i = 0; i < BarCount; i++)
        {
            double target = _targetLevels[i];

            // Rise fast, fall slowly → looks smooth instead of jittery.
            _shownLevels[i] = target > _shownLevels[i]
                ? _shownLevels[i] + (target - _shownLevels[i]) * 0.6
                : _shownLevels[i] * 0.85;

            _bars[i].Height = Math.Max(4, _shownLevels[i] * maxHeight);
        }

        // Cover art "pulses" with the bass (first few bars).
        double bass = (_shownLevels[0] + _shownLevels[1] + _shownLevels[2] + _shownLevels[3]) / 4;
        ArtScale.ScaleX = ArtScale.ScaleY = 1 + bass * 0.06;
    }

    // ───────────── Progress & volume sliders ─────────────

    private void ProgressSlider_DragStarted(object sender, DragStartedEventArgs e) => _isDraggingSlider = true;

    private void ProgressSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingSlider = false;
        _player.Position = TimeSpan.FromSeconds(ProgressSlider.Value);
    }

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingSlider) return; // change came from the timer, not the user

        CurrentTimeText.Text = Format(TimeSpan.FromSeconds(e.NewValue));

        // A click on the bar (not a drag) → seek immediately.
        if (!_isDraggingSlider)
            _player.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _player.Volume = (float)e.NewValue; // 0.0 – 1.0
    }

    // ───────────── Helpers ─────────────

    private void SetSliderSilently(double value)
    {
        _isUpdatingSlider = true;
        ProgressSlider.Value = value;
        _isUpdatingSlider = false;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
