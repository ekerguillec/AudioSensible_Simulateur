using System;
using System.Collections.Generic;

namespace HearingLossSimulator
{
    public record DeviceInfo(string Name, string Description);

    public interface IAudioDevice : IDisposable
    {
        uint SampleRate { get; }
        uint Channels { get; }
        ulong PeriodSize { get; }
        ulong BufferSize { get; }
        void Start();
        int Read(short[] buffer, int frames);
        int Write(short[] buffer, int frames);
    }
}
