using NAudio.Wave;
using System.IO;

namespace Noise
{
    // I'll make a better and good one laterrrr
    public class SoundHandle
    {
        private readonly WaveOut _waveOut;
        private readonly WaveFileReader _reader;
        private bool _disposed;
        private bool _looping;

        public SoundHandle(WaveOut waveOut, WaveFileReader reader)
        {
            _waveOut = waveOut;
            _reader = reader;
            _waveOut.PlaybackStopped += OnPlaybackStopped;
        }

        public static SoundHandle Create(Stream wavStream)
        {
            WaveFileReader reader = new WaveFileReader(wavStream);
            WaveOut waveOut = new WaveOut();

            waveOut.Init(reader);

            return new SoundHandle(waveOut, reader);
        }

        public void Play() => _waveOut.Play();
        public void Pause() => _waveOut.Pause();

        public void PlayLooping()
        {
            _looping = true;
            _waveOut.Play();
        }

        private void OnPlaybackStopped(object s, StoppedEventArgs e)
        {
            _reader.Position = 0;
            if (_looping && !_disposed)
            {
                _waveOut.Play();
                return;
            }
            //DisposeOnce();
        }

        public void Stop()
        {
            _looping = false;
            try { _waveOut.Stop(); } catch { }
            DisposeOnce();
        }

        private void DisposeOnce()
        {
            if (_disposed) return;
            _disposed = true;
            _waveOut.Dispose();
            _reader.Dispose();
        }
    }
}
