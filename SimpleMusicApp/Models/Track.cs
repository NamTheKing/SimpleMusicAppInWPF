using System.ComponentModel;
using System.IO;

namespace SimpleMusicApp.Models;

/// <summary>One song in the playlist.</summary>
public class Track : INotifyPropertyChanged
{
    private bool _isPlaying;

    public Track(string filePath)
    {
        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
    }

    public string FilePath { get; }
    public string Title { get; }

    /// <summary>True for the song currently loaded → the list shows it in green.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
