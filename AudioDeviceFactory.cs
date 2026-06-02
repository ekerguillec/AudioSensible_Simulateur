using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HearingLossSimulator
{
    public static class AudioDeviceFactory
    {
        private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static List<DeviceInfo> EnumerateDevices(bool capture)
        {
            if (IsLinux) return AlsaNative.EnumerateDevices(capture);
            if (IsWindows) return WinAudioHelper.EnumerateDevices(capture);
            return OpenALHelper.EnumerateDevices(capture);
        }

        public static IAudioDevice Create(string name, bool capture,
            uint rate, uint chan, ulong period, ulong buffer)
        {
            if (IsLinux) return new AlsaDevice(name, capture, rate, chan, period, buffer);
            if (IsWindows) return new WinAudioDevice(name, capture, rate, chan, period, buffer);
            return new OpenALDevice(name, capture, rate, chan, period, buffer);
        }
    }
}
