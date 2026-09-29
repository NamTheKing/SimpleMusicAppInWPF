using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SimpleMusicApp.Models;

namespace SimpleMusicApp;

public partial class MainWindow : Window
{
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };

    // MediaPlayer = audio-only player (no visual element needed, unlike MediaElement).
    private readonly MediaPlayer _player = new();

    // Ticks every 500 ms to move the progress slider while a song plays.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    // ObservableCollection notifies the ListBox automatically when songs are added/removed.
    private readonly ObservableCollection<Track> _playlist = new();

    private int _currentIndex = -1;
    private bool _isPlaying;
    private bool _isDraggingSlider;    // user is holding the progress thumb
    private bool _isUpdatingSlider;    // the timer (not the user) is changing the slider

    public MainWindow()
    {
        InitializeComponent();

        PlaylistBox.ItemsSource = _playlist;
        _player.Volume = VolumeSlider.Value;

        _player.MediaOpened += Player_MediaOpened;
        _player.MediaEnded += (_, _) => PlayNext();
        _player.MediaFailed += (_, e) =>
            MessageBox.Show($"Cannot play this file:\n{e.ErrorException.Message}", "Error");

        _timer.Tick += Timer_Tick;
    }

    // ───────────── Playlist ─────────────

    private void AddSongs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose songs",
            Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
            AddFiles(dialog.FileNames);
    }

    // Drag & drop files from Explorer onto the window.
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            AddFiles(files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
    }

    private void AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
            _playlist.Add(new Track(file));

        // Auto-select the first song so "Play" works right away.
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

            if (index == _currentIndex) Stop();          // removed the playing song
            else if (index < _currentIndex) _currentIndex--; // keep pointing at the same song
        }
    }

    // ───────────── Playback ─────────────

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;

        _currentIndex = index;
        var track = _playlist[index];

        _player.Open(new Uri(track.FilePath)); // loads asynchronously → MediaOpened fires later
        _player.Play();
        _isPlaying = true;
        _timer.Start();

        PlaylistBox.SelectedIndex = index;
        NowPlayingText.Text = track.Title;
        PlayPauseButton.Content = "⏸";
    }

    private void Player_MediaOpened(object? sender, EventArgs e)
    {
        // Duration is only known after the file has been opened.
        if (_player.NaturalDuration.HasTimeSpan)
        {
            var total = _player.NaturalDuration.TimeSpan;
            ProgressSlider.Maximum = total.TotalSeconds;
            TotalTimeText.Text = Format(total);
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < 0)
        {
            // Nothing loaded yet → start the selected (or first) song.
            PlayTrack(Math.Max(PlaylistBox.SelectedIndex, 0));
            return;
        }

        if (_isPlaying)
        {
            _player.Pause();
            _timer.Stop();
            PlayPauseButton.Content = "▶";
        }
        else
        {
            _player.Play();
            _timer.Start();
            PlayPauseButton.Content = "⏸";
        }
        _isPlaying = !_isPlaying;
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();

    private void Stop()
    {
        _player.Stop();
        _timer.Stop();
        _isPlaying = false;
        _currentIndex = -1;

        PlayPauseButton.Content = "▶";
        NowPlayingText.Text = "Stopped";
        SetSliderSilently(0);
        CurrentTimeText.Text = "00:00";
    }

    private void Next_Click(object sender, RoutedEventArgs e) => PlayNext();

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;

        // Like most players: if >3 s into the song, restart it; otherwise go to the previous one.
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

    // ───────────── Progress & volume ─────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider) return; // don't fight the user's mouse

        SetSliderSilently(_player.Position.TotalSeconds);
        CurrentTimeText.Text = Format(_player.Position);
    }

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

        // A click on the track (not a drag) → seek immediately.
        if (!_isDraggingSlider)
            _player.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _player.Volume = e.NewValue; // 0.0 – 1.0
    }

    // ───────────── Helpers ─────────────

    private void SetSliderSilently(double value)
    {
        _isUpdatingSlider = true;
        ProgressSlider.Value = value;
        _isUpdatingSlider = false;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
}
