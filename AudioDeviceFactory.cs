using System.Collections.Generic;

namespace HearingLossSimulator
{
    public static class AudioDeviceFactory
    {
        public static List<DeviceInfo> EnumerateDevices(bool capture)
            => AlsaNative.EnumerateDevices(capture);

        public static IAudioDevice Create(string name, bool capture,
            uint rate, uint chan, ulong period, ulong buffer)
            => new AlsaDevice(name, capture, rate, chan, period, buffer);
    }
}
