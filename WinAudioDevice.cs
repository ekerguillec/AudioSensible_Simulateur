using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace HearingLossSimulator
{
    internal static class WinMM
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEINCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEOUTCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
            public uint dwSupport;
        }

        public const int WAVE_FORMAT_PCM = 1;
        public const uint WHDR_DONE = 0x00000001;
        public const uint WHDR_INQUEUE = 0x00000010;
        public const int CALLBACK_EVENT = 0x00050000;
        public const int INFINITE = -1;
        public const int WAIT_OBJECT_0 = 0;

        // waveIn
        [DllImport("winmm.dll")] public static extern int waveInGetNumDevs();
        [DllImport("winmm.dll")] public static extern int waveInGetDevCapsW(int id, ref WAVEINCAPS c, int sz);
        [DllImport("winmm.dll")] public static extern int waveInOpen(out IntPtr h, int id, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] public static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] public static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] public static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] public static extern int waveInClose(IntPtr h);

        // waveOut
        [DllImport("winmm.dll")] public static extern int waveOutGetNumDevs();
        [DllImport("winmm.dll")] public static extern int waveOutGetDevCapsW(int id, ref WAVEOUTCAPS c, int sz);
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr h, int id, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr h);

        // kernel32
        [DllImport("kernel32.dll")] public static extern IntPtr CreateEvent(IntPtr attr, bool manualReset, bool initial, string? name);
        [DllImport("kernel32.dll")] public static extern int WaitForSingleObject(IntPtr h, int ms);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    }

    public static class WinAudioHelper
    {
        public static List<DeviceInfo> EnumerateDevices(bool capture)
        {
            var devices = new List<DeviceInfo>();
            int hdrSz = Marshal.SizeOf<WinMM.WAVEHDR>();
            if (capture)
            {
                for (int i = 0; i < WinMM.waveInGetNumDevs(); i++)
                {
                    var caps = new WinMM.WAVEINCAPS();
                    if (WinMM.waveInGetDevCapsW(i, ref caps, Marshal.SizeOf<WinMM.WAVEINCAPS>()) == 0)
                        devices.Add(new DeviceInfo(i.ToString(), caps.szPname));
                }
            }
            else
            {
                for (int i = 0; i < WinMM.waveOutGetNumDevs(); i++)
                {
                    var caps = new WinMM.WAVEOUTCAPS();
                    if (WinMM.waveOutGetDevCapsW(i, ref caps, Marshal.SizeOf<WinMM.WAVEOUTCAPS>()) == 0)
                        devices.Add(new DeviceInfo(i.ToString(), caps.szPname));
                }
            }
            if (devices.Count == 0)
                devices.Add(new DeviceInfo("0", "Périphérique par défaut"));
            return devices;
        }
    }

    public class WinAudioDevice : IAudioDevice
    {
        private readonly bool isCapture;
        private readonly uint sampleRate;
        private readonly uint channels;
        private readonly ulong periodSize;
        private readonly ulong bufferSize;

        public uint SampleRate => sampleRate;
        public uint Channels => channels;
        public ulong PeriodSize => periodSize;
        public ulong BufferSize => bufferSize;

        private IntPtr handle;
        private IntPtr eventHandle;

        private const int N = 2;
        private WinMM.WAVEHDR[] headers = new WinMM.WAVEHDR[N];
        private GCHandle pinnedHeaders;
        private IntPtr[] hdrPtrs = new IntPtr[N];
        private short[][] data = new short[N][];
        private GCHandle[] dataPins = new GCHandle[N];

        private readonly Queue<short> captureQueue = new();
        private readonly object qLock = new();

        public WinAudioDevice(string deviceName, bool capture, uint rate, uint chan, ulong period, ulong buffer)
        {
            isCapture = capture;
            sampleRate = rate;
            channels = chan;
            periodSize = period;
            bufferSize = buffer;

            int devId = int.TryParse(deviceName, out int n) ? n : 0;
            int bytesPerFrame = (int)chan * 2;
            int bufBytes = (int)period * bytesPerFrame;

            var fmt = new WinMM.WAVEFORMATEX
            {
                wFormatTag = WinMM.WAVE_FORMAT_PCM,
                nChannels = (ushort)chan,
                nSamplesPerSec = rate,
                wBitsPerSample = 16,
                nBlockAlign = (ushort)bytesPerFrame,
                nAvgBytesPerSec = rate * (uint)bytesPerFrame
            };

            eventHandle = WinMM.CreateEvent(IntPtr.Zero, false, false, null);

            int r;
            if (capture)
                r = WinMM.waveInOpen(out handle, devId, ref fmt, eventHandle, IntPtr.Zero, WinMM.CALLBACK_EVENT);
            else
                r = WinMM.waveOutOpen(out handle, devId, ref fmt, eventHandle, IntPtr.Zero, WinMM.CALLBACK_EVENT);

            if (r != 0) throw new Exception($"Impossible d'ouvrir le périphérique audio Windows (code {r})");

            // Pin headers array so WinMM can write back dwFlags/dwBytesRecorded in place
            pinnedHeaders = GCHandle.Alloc(headers, GCHandleType.Pinned);
            unsafe
            {
                fixed (WinMM.WAVEHDR* p = headers)
                    for (int i = 0; i < N; i++)
                        hdrPtrs[i] = (IntPtr)(p + i);
            }

            int hdrSz = Marshal.SizeOf<WinMM.WAVEHDR>();
            for (int i = 0; i < N; i++)
            {
                data[i] = new short[bufBytes / 2];
                dataPins[i] = GCHandle.Alloc(data[i], GCHandleType.Pinned);
                headers[i].lpData = dataPins[i].AddrOfPinnedObject();
                headers[i].dwBufferLength = (uint)bufBytes;

                if (capture)
                {
                    WinMM.waveInPrepareHeader(handle, hdrPtrs[i], hdrSz);
                    WinMM.waveInAddBuffer(handle, hdrPtrs[i], hdrSz);
                }
                else
                {
                    WinMM.waveOutPrepareHeader(handle, hdrPtrs[i], hdrSz);
                    // Playback buffers start as "done" so Write() can use them immediately
                    headers[i].dwFlags = WinMM.WHDR_DONE;
                }
            }
        }

        public void Start()
        {
            if (isCapture) WinMM.waveInStart(handle);
        }

        public int Read(short[] buffer, int frames)
        {
            if (!isCapture) return 0;

            int needed = frames * (int)channels;
            int hdrSz = Marshal.SizeOf<WinMM.WAVEHDR>();

            while (true)
            {
                // Drain completed buffers into queue
                for (int i = 0; i < N; i++)
                {
                    if ((headers[i].dwFlags & WinMM.WHDR_DONE) != 0)
                    {
                        int got = (int)(headers[i].dwBytesRecorded / 2);
                        lock (qLock)
                            for (int j = 0; j < got; j++)
                                captureQueue.Enqueue(data[i][j]);

                        headers[i].dwFlags = 0;
                        headers[i].dwBytesRecorded = 0;
                        WinMM.waveInUnprepareHeader(handle, hdrPtrs[i], hdrSz);
                        WinMM.waveInPrepareHeader(handle, hdrPtrs[i], hdrSz);
                        WinMM.waveInAddBuffer(handle, hdrPtrs[i], hdrSz);
                    }
                }

                lock (qLock)
                {
                    if (captureQueue.Count >= needed)
                    {
                        for (int i = 0; i < needed; i++)
                            buffer[i] = captureQueue.Dequeue();
                        return frames;
                    }
                }

                WinMM.WaitForSingleObject(eventHandle, 20);
            }
        }

        public int Write(short[] buffer, int frames)
        {
            if (isCapture) return 0;

            int hdrSz = Marshal.SizeOf<WinMM.WAVEHDR>();
            int sampleCount = frames * (int)channels;

            // Find a free buffer (WHDR_DONE = finished playing or never queued)
            int idx = -1;
            while (idx < 0)
            {
                for (int i = 0; i < N; i++)
                {
                    if ((headers[i].dwFlags & WinMM.WHDR_INQUEUE) == 0)
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx < 0)
                    WinMM.WaitForSingleObject(eventHandle, 10);
            }

            // Unqueue if previously played
            if ((headers[idx].dwFlags & WinMM.WHDR_DONE) != 0)
                WinMM.waveOutUnprepareHeader(handle, hdrPtrs[idx], hdrSz);

            Array.Copy(buffer, data[idx], sampleCount);
            headers[idx].dwBufferLength = (uint)(sampleCount * 2);
            headers[idx].dwFlags = 0;

            WinMM.waveOutPrepareHeader(handle, hdrPtrs[idx], hdrSz);
            WinMM.waveOutWrite(handle, hdrPtrs[idx], hdrSz);

            return frames;
        }

        public void Dispose()
        {
            int hdrSz = Marshal.SizeOf<WinMM.WAVEHDR>();
            try
            {
                if (isCapture)
                {
                    WinMM.waveInStop(handle);
                    WinMM.waveInReset(handle);
                    for (int i = 0; i < N; i++)
                        WinMM.waveInUnprepareHeader(handle, hdrPtrs[i], hdrSz);
                    WinMM.waveInClose(handle);
                }
                else
                {
                    WinMM.waveOutReset(handle);
                    for (int i = 0; i < N; i++)
                        if ((headers[i].dwFlags & WinMM.WHDR_DONE) != 0 || (headers[i].dwFlags & WinMM.WHDR_INQUEUE) == 0)
                            WinMM.waveOutUnprepareHeader(handle, hdrPtrs[i], hdrSz);
                    WinMM.waveOutClose(handle);
                }
            }
            finally
            {
                if (eventHandle != IntPtr.Zero) WinMM.CloseHandle(eventHandle);
                if (pinnedHeaders.IsAllocated) pinnedHeaders.Free();
                for (int i = 0; i < N; i++)
                    if (dataPins[i].IsAllocated) dataPins[i].Free();
            }
        }
    }
}
