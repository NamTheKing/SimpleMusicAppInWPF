using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SimpleMusicApp.Audio;

/// <summary>
/// Small wrapper around NAudio: open / play / pause / seek / volume,
/// plus <see cref="GetSpectrum"/> which tells how loud each frequency band is right now.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private const int FftSize = 1024;  // must be a power of 2
    private const int FftPower = 10;   // 2^10 = 1024

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private SampleCapture? _capture;
    private VolumeSampleProvider? _volumeProvider;
    private float _volume = 0.5f;

    private readonly float[] _samples = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];

    /// <summary>Raised when the song finishes by itself.</summary>
    public event EventHandler? TrackEnded;

    public bool IsLoaded => _reader != null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (_reader != null) _reader.CurrentTime = value; }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            if (_volumeProvider != null) _volumeProvider.Volume = value;
        }
    }

    public void Open(string path)
    {
        Close();

        // Pipeline: file → capture (for visualizer) → volume → sound card
        _reader = new AudioFileReader(path);
        _capture = new SampleCapture(_reader, FftSize);
        _volumeProvider = new VolumeSampleProvider(_capture) { Volume = _volume };

        _output = new WaveOutEvent { DesiredLatency = 100 }; // low latency keeps bars in sync with sound
        _output.Init(_volumeProvider);
        _output.PlaybackStopped += Output_PlaybackStopped;
    }

    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // We unsubscribe before stopping manually (see Close), so this only means "song ended".
        TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Fills <paramref name="bands"/> with values 0..1 = loudness of each frequency band,
    /// from bass (index 0) to treble (last index).
    /// </summary>
    public void GetSpectrum(float[] bands)
    {
        if (_capture == null || _reader == null || !IsPlaying)
        {
            Array.Clear(bands);
            return;
        }

        // 1) Take the latest samples and apply a Hann window (smooths the edges → cleaner FFT).
        _capture.CopyLatest(_samples);
        for (int i = 0; i < FftSize; i++)
        {
            _fft[i].X = (float)(_samples[i] * FastFourierTransform.HannWindow(i, FftSize));
            _fft[i].Y = 0;
        }

        // 2) FFT: turns "sound over time" into "how much of each frequency".
        FastFourierTransform.FFT(true, FftPower, _fft);

        // 3) Group FFT bins into bands on a log scale (like our ears: bass gets more bars).
        double binHz = (double)_reader.WaveFormat.SampleRate / FftSize;
        int minBin = Math.Max(1, (int)(60 / binHz));                        // ~60 Hz
        int maxBin = Math.Min(FftSize / 2 - 1, (int)(16000 / binHz));       // ~16 kHz
        double ratio = (double)maxBin / minBin;

        for (int b = 0; b < bands.Length; b++)
        {
            int start = (int)(minBin * Math.Pow(ratio, (double)b / bands.Length));
            int end = Math.Max(start + 1, (int)(minBin * Math.Pow(ratio, (double)(b + 1) / bands.Length)));

            double peak = 0;
            for (int k = start; k < end; k++)
            {
                double magnitude = Math.Sqrt(_fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y);
                peak = Math.Max(peak, magnitude);
            }

            // 4) Convert to decibels and map roughly -70 dB..-10 dB → 0..1.
            //    Treble is naturally quieter, so give it a small boost.
            double db = 20 * Math.Log10(peak + 1e-9);
            double level = (db + 70) / 60 + 0.2 * b / bands.Length;
            bands[b] = (float)Math.Clamp(level, 0, 1);
        }
    }

    public void Close()
    {
        if (_output != null)
        {
            _output.PlaybackStopped -= Output_PlaybackStopped;
            _output.Dispose();
            _output = null;
        }
        _reader?.Dispose();
        _reader = null;
        _capture = null;
        _volumeProvider = null;
    }

    public void Dispose() => Close();
}
