using System.Runtime.InteropServices;

namespace BreaksForEyes.Services;

/// <summary>
/// 轻量音频活动检测：读取默认播放设备的峰值电平，用于 FR-10 的
/// "全屏 + 音频回放活跃" 视频播放启发式判断。仅使用系统自带的 Core Audio COM 接口，
/// 不引入任何 NuGet 依赖。
/// </summary>
public sealed class AudioActivityDetector
{
    private const int ERender = 0;
    private const int EConsole = 0;
    private const int ClsCtxInprocServer = 0x1;

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            int dwClsCtx,
            IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);

        [PreserveSig]
        int GetMeteringChannelCount(out int pnChannelCount);

        [PreserveSig]
        int GetChannelsPeakValues(int channelCount, [Out] float[] peakValues);

        [PreserveSig]
        int QueryHardwareSupport(out int pdwHardwareSupportMask);

        [PreserveSig]
        int GetHardwareSupportMask(out int pdwHardwareSupportMask);
    }

    /// <summary>返回默认输出设备的峰值（0.0 - 1.0）。设备不可用或读取失败时返回 0。</summary>
    public float GetPeakValue()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioMeterInformation? meter = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(ERender, EConsole, out device);
            if (hr != 0 || device is null)
            {
                return 0;
            }

            var meterIid = typeof(IAudioMeterInformation).GUID;
            hr = device.Activate(
                ref meterIid,
                ClsCtxInprocServer,
                IntPtr.Zero,
                out object? meterObject);

            if (hr != 0 || meterObject is not IAudioMeterInformation meterInterface)
            {
                return 0;
            }

            meter = meterInterface;
            if (meter.GetPeakValue(out float peak) != 0)
            {
                return 0;
            }

            return float.IsFinite(peak) ? Math.Clamp(peak, 0f, 1f) : 0f;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (meter is not null)
            {
                Marshal.ReleaseComObject(meter);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
    }
}
