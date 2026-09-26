using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
namespace FixedVolumeMixer {
    static class MixerSessionProbe {
        [MTAThread] static void Main(string[] args) {
            var e=(IDeviceEnumerator)new DeviceEnumerator();IDevice d;Marshal.ThrowExceptionForHR(e.GetDefaultAudioEndpoint(0,1,out d));
            Guid iid=typeof(ISessionManager).GUID;object raw;Marshal.ThrowExceptionForHR(d.Activate(ref iid,23,IntPtr.Zero,out raw));
            ISimpleVolume volume;Guid session=Guid.Empty;Marshal.ThrowExceptionForHR(((ISessionManager)raw).GetSimpleAudioVolume(ref session,0,out volume));
            Thread.Sleep(400);float actual;volume.GetMasterVolume(out actual);File.WriteAllText(args[0],actual.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if(args.Length>1){Guid context=Guid.Empty;volume.SetMasterVolume(.17f,ref context);}
            Thread.Sleep(700);
        }
    }
}
