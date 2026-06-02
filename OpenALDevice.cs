using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Audio.OpenAL;

namespace HearingLossSimulator
{
    public static class OpenALHelper
    {
        public static List<DeviceInfo> EnumerateDevices(bool capture)
        {
            var devices = new List<DeviceInfo>();
            try
            {
                var specifier = capture
                    ? GetEnumerationStringList.CaptureDeviceSpecifier
                    : GetEnumerationStringList.DeviceSpecifier;

                foreach (var name in ALC.GetStringList(specifier))
                {
                    if (!string.IsNullOrEmpty(name))
                        devices.Add(new DeviceInfo(name, name));
                }
            }
            catch { }

            if (devices.Count == 0)
                devices.Add(new DeviceInfo("default", "Périphérique par défaut"));

            return devices;
        }
    }

    public class OpenALDevice : IAudioDevice
    {
        private readonly bool isCapture;
        private readonly uint sampleRate;
        private readonly uint channels;
        private readonly ulong periodSize;
        private readonly ulong bufferSize;
        private readonly ALFormat format;

        // Capture
        private ALCaptureDevice captureDevice;

        // Playback
        private ALDevice playbackDevice;
        private ALContext playbackContext;
        private int alSource;
        private int[] alBuffers = Array.Empty<int>();
        private const int NUM_BUFFERS = 3;

        public uint SampleRate => sampleRate;
        public uint Channels => channels;
        public ulong PeriodSize => periodSize;
        public ulong BufferSize => bufferSize;

        public OpenALDevice(string deviceName, bool capture, uint rate, uint chan, ulong period, ulong buffer)
        {
            isCapture = capture;
            sampleRate = rate;
            channels = chan;
            periodSize = period;
            bufferSize = buffer;
            format = chan == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;

            string? openAlName = (deviceName == "default" || string.IsNullOrEmpty(deviceName))
                ? null : deviceName;

            if (capture)
                InitCapture(openAlName);
            else
                InitPlayback(openAlName);
        }

        private void InitCapture(string? deviceName)
        {
            captureDevice = ALC.CaptureOpenDevice(deviceName, (int)sampleRate, format, (int)(periodSize * 4));
            if (captureDevice == ALCaptureDevice.Null)
                throw new Exception(
                    "Impossible d'ouvrir le périphérique de capture OpenAL.\n" +
                    "Vérifiez les permissions microphone dans Préférences Système > Sécurité et confidentialité.");
        }

        private void InitPlayback(string? deviceName)
        {
            playbackDevice = ALC.OpenDevice(deviceName);
            if (playbackDevice == ALDevice.Null)
                throw new Exception("Impossible d'ouvrir le périphérique de lecture OpenAL.");

            playbackContext = ALC.CreateContext(playbackDevice, Array.Empty<int>());
            ALC.MakeContextCurrent(playbackContext);

            alSource = AL.GenSource();
            alBuffers = new int[NUM_BUFFERS];
            AL.GenBuffers(alBuffers);

            // Pre-fill with silence so Write() has buffers to recycle immediately
            short[] silence = new short[(int)periodSize * (int)channels];
            foreach (int buf in alBuffers)
            {
                AL.BufferData(buf, format, silence, (int)sampleRate);
                AL.SourceQueueBuffer(alSource, buf);
            }

            AL.SourcePlay(alSource);
        }

        public void Start()
        {
            if (isCapture)
                ALC.CaptureStart(captureDevice);
        }

        public int Read(short[] buffer, int frames)
        {
            if (!isCapture) return 0;

            int[] count = new int[1];
            while (count[0] < frames)
            {
                ALC.GetInteger(captureDevice, AlcGetInteger.CaptureSamples, 1, count);
                if (count[0] < frames)
                    Thread.Sleep(1);
            }

            ALC.CaptureSamples(captureDevice, buffer, frames);
            return frames;
        }

        public int Write(short[] buffer, int frames)
        {
            if (isCapture) return 0;

            // Ensure context is current on the calling thread (may differ from init thread)
            ALC.MakeContextCurrent(playbackContext);

            int processed = 0;
            int wait = 0;
            while (processed == 0)
            {
                AL.GetSource(alSource, ALGetSourcei.BuffersProcessed, out processed);
                if (processed == 0)
                {
                    Thread.Sleep(1);
                    if (++wait > 500) return 0;
                }
            }

            int[] unqueued = new int[1];
            AL.SourceUnqueueBuffers(alSource, 1, unqueued);

            AL.BufferData(unqueued[0], format, buffer.AsSpan(0, frames * (int)channels), (int)sampleRate);
            AL.SourceQueueBuffer(alSource, unqueued[0]);

            // Restart source if it stalled due to buffer underrun
            AL.GetSource(alSource, ALGetSourcei.SourceState, out int state);
            if ((ALSourceState)state != ALSourceState.Playing)
                AL.SourcePlay(alSource);

            return frames;
        }

        public void Dispose()
        {
            if (isCapture)
            {
                ALC.CaptureStop(captureDevice);
                ALC.CaptureCloseDevice(captureDevice);
            }
            else
            {
                ALC.MakeContextCurrent(playbackContext);
                AL.SourceStop(alSource);
                AL.DeleteSource(alSource);
                if (alBuffers.Length > 0)
                    AL.DeleteBuffers(alBuffers);
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(playbackContext);
                ALC.CloseDevice(playbackDevice);
            }
        }
    }
}
