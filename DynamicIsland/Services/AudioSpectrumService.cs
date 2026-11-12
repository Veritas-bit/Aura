using System.Numerics;
using System.Runtime.InteropServices;
using NAudio;
using NAudio.Dmo;
using NAudio.Wave;

namespace DynamicIsland.Services;

public sealed class AudioSpectrumService : IDisposable
{
    private const int FftSize = 1024;
    private const int BandCount = 48;
    private readonly float[] _samples = new float[FftSize];
    private float[] _levels = new float[BandCount];
    private WasapiLoopbackCapture? _capture;
    private int _sampleCount;
    private bool _running;
    private bool _disposed;
    private bool _formatErrorReported;

    public event Action<float[]>? LevelsUpdated;
    public event Action<string>? ErrorOccurred;

    public void Start()
    {
        if (_disposed || _running) return;
        try
        {
            _capture ??= new WasapiLoopbackCapture();
            _capture.DataAvailable -= Capture_DataAvailable;
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.StartRecording();
            _running = true;
            _formatErrorReported = false;
        }
        catch (COMException ex)
        {
            ErrorOccurred?.Invoke($"Не удалось включить анализ аудио: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke($"Не удалось включить анализ аудио: {ex.Message}");
        }
        catch (MmException ex)
        {
            ErrorOccurred?.Invoke($"Не удалось включить анализ аудио: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_running || _capture is null) return;
        _running = false;
        _capture.StopRecording();
        _sampleCount = 0;
        Array.Clear(_levels);
        LevelsUpdated?.Invoke((float[])_levels.Clone());
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format is null) return;
        int bytesPerSample = format.BitsPerSample / 8;
        int frameSize = format.BlockAlign;
        if (format.Channels <= 0 || bytesPerSample is not (2 or 3 or 4) || frameSize <= 0 ||
            e.Buffer.Length < e.BytesRecorded)
        {
            ReportFormatError();
            return;
        }

        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       format is WaveFormatExtensible extensible &&
                       extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        for (int frame = 0; frame + frameSize <= e.BytesRecorded; frame += frameSize)
        {
            double sum = 0;
            for (int channel = 0; channel < format.Channels; channel++)
            {
                int offset = frame + channel * bytesPerSample;
                sum += ReadSample(e.Buffer, offset, format.BitsPerSample, isFloat);
            }

            _samples[_sampleCount++] = (float)(sum / format.Channels);
            if (_sampleCount == FftSize)
            {
                AnalyzeWindow();
                Array.Copy(_samples, FftSize / 2, _samples, 0, FftSize / 2);
                _sampleCount = FftSize / 2;
            }
        }
    }

    private void ReportFormatError()
    {
        if (_formatErrorReported) return;
        _formatErrorReported = true;
        ErrorOccurred?.Invoke("Формат системного аудио не поддерживается эквалайзером.");
    }

    private static double ReadSample(byte[] buffer, int offset, int bits, bool isFloat)
    {
        if (bits == 32 && isFloat)
            return BitConverter.ToSingle(buffer, offset);

        return bits switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768.0,
            24 => SignExtend24(buffer, offset) / 8388608.0,
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648.0,
            _ => 0
        };
    }

    private static int SignExtend24(byte[] buffer, int offset)
    {
        int value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
    }

    private void AnalyzeWindow()
    {
        var fft = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            double window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
            fft[i] = new Complex(_samples[i] * window, 0);
        }
        Transform(fft);

        var levels = new float[BandCount];
        double logMin = Math.Log(2);
        double logMax = Math.Log(FftSize / 2);
        for (int band = 0; band < BandCount; band++)
        {
            int first = Math.Max(1, (int)Math.Exp(logMin + (logMax - logMin) * band / BandCount));
            int last = Math.Max(first + 1, (int)Math.Exp(logMin + (logMax - logMin) * (band + 1) / BandCount));
            last = Math.Min(last, FftSize / 2);
            double energy = 0;
            for (int bin = first; bin < last; bin++)
                energy += fft[bin].Magnitude * fft[bin].Magnitude;

            double amplitude = Math.Sqrt(energy / Math.Max(1, last - first)) * (4.0 / FftSize);
            double decibels = 20 * Math.Log10(amplitude + 1e-8);
            levels[band] = (float)Math.Clamp((decibels + 60) / 52, 0, 1);
        }

        _levels = levels;
        LevelsUpdated?.Invoke((float[])levels.Clone());
    }

    private static void Transform(Complex[] data)
    {
        int j = 0;
        for (int i = 1; i < data.Length; i++)
        {
            int bit = data.Length >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }

        for (int length = 2; length <= data.Length; length <<= 1)
        {
            var root = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (int start = 0; start < data.Length; start += length)
            {
                Complex factor = Complex.One;
                for (int offset = 0; offset < length / 2; offset++)
                {
                    Complex even = data[start + offset];
                    Complex odd = data[start + offset + length / 2] * factor;
                    data[start + offset] = even + odd;
                    data[start + offset + length / 2] = even - odd;
                    factor *= root;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        if (_capture is not null)
        {
            _capture.DataAvailable -= Capture_DataAvailable;
            _capture.Dispose();
            _capture = null;
        }
    }
}
