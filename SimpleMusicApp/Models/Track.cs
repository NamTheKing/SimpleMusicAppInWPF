using System.IO;

namespace SimpleMusicApp.Models;

/// <summary>One song in the playlist.</summary>
public class Track
{
    public Track(string filePath)
    {
        FilePath = filePath;
        Title = Path.GetFileNameWithoutExtension(filePath);
    }

    public string FilePath { get; }
    public string Title { get; }
}
