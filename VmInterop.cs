using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VoicemeeterSliderControl
{
    public static class VmInterop
    {
        [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetVoicemeeterType")]
        static extern long GetType64(ref long pType);

        [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetParameterStringW", CharSet = CharSet.Unicode)]
        static extern long GetParamStringW64(
            [MarshalAs(UnmanagedType.LPStr)] string szParamName,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder szString);

        public static string GetVoicemeeterKindName()
        {
            long type = 0;
            try
            {
                long res = GetType64(ref type);
                if (res != 0) return "неизвестно (код ошибки)";
            }
            catch (DllNotFoundException) { return "неизвестно (DLL не найдена)"; }
            return type switch { 1 => "Basic", 2 => "Banana", 3 => "Potato", 6 => "Potato x64", _ => $"неизвестно (код {type})" };
        }

        public static string TryGetLabel(string paramName)
        {
            var sb = new StringBuilder(512);
            try
            {
                long res = GetParamStringW64(paramName, sb);
                if (res != 0) return null;
            }
            catch { return null; }
            return sb.ToString();
        }

        public class ChannelInfo
        {
            public string Kind;
            public int Index;
            public string Label;
            public string ParamPrefix => $"{Kind}[{Index}]";
        }

        public static List<ChannelInfo> DiscoverChannels()
        {
            var result = new List<ChannelInfo>();
            for (int i = 0; i < 16; i++)
            {
                string label = TryGetLabel($"Strip[{i}].Label");
                if (label == null) break;
                result.Add(new ChannelInfo { Kind = "Strip", Index = i, Label = string.IsNullOrWhiteSpace(label) ? $"Strip {i}" : label });
            }
            for (int i = 0; i < 8; i++)
            {
                string label = TryGetLabel($"Bus[{i}].Label");
                if (label == null) break;
                result.Add(new ChannelInfo { Kind = "Bus", Index = i, Label = string.IsNullOrWhiteSpace(label) ? $"Bus {i}" : label });
            }
            return result;
        }
    }
}