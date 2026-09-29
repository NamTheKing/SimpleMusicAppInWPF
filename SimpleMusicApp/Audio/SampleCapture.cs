using NAudio.Wave;

namespace SimpleMusicApp.Audio;

/// <summary>
/// Sits in the audio pipeline: passes samples through unchanged,
/// but keeps a copy of the latest ones so the visualizer can analyse them.
///   file → [SampleCapture] → volume → speakers
/// </summary>
internal sealed class SampleCapture : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _ring;   // circular buffer of the latest mono samples
    private int _writePos;

    public SampleCapture(ISampleProvider source, int size)
    {
        _source = source;
        _ring = new float[size];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    // Called by the audio thread whenever it needs more sound.
    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        int channels = WaveFormat.Channels;

        lock (_ring)
        {
            // Mix stereo (L,R,L,R,...) down to mono and store it.
            for (int i = 0; i + channels <= read; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += buffer[offset + i + c];

                _ring[_writePos] = sum / channels;
                _writePos = (_writePos + 1) % _ring.Length;
            }
        }
        return read;
    }

    /// <summary>Copies the most recent samples (oldest first) into <paramref name="dest"/>.</summary>
    public void CopyLatest(float[] dest)
    {
        lock (_ring)
        {
            int start = (_writePos - dest.Length + _ring.Length) % _ring.Length;
            for (int i = 0; i < dest.Length; i++)
                dest[i] = _ring[(start + i) % _ring.Length];
        }
    }
}
