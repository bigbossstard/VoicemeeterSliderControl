using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VoicemeeterSliderControl
{
    public static class VmInterop
    {
        [DllImport(
            "VoicemeeterRemote.dll",
            EntryPoint = "VBVMR_GetVoicemeeterType",
            CallingConvention = CallingConvention.StdCall)]
        static extern int GetType32(ref int pType);

        [DllImport(
            "VoicemeeterRemote64.dll",
            EntryPoint = "VBVMR_GetVoicemeeterType",
            CallingConvention = CallingConvention.StdCall)]
        static extern int GetType64(ref int pType);

        [DllImport(
            "VoicemeeterRemote.dll",
            EntryPoint = "VBVMR_GetParameterStringW",
            CallingConvention = CallingConvention.StdCall,
            CharSet = CharSet.Unicode)]
        static extern int GetParamStringW32(
            [MarshalAs(UnmanagedType.LPStr)] string szParamName,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder szString);

        [DllImport(
            "VoicemeeterRemote64.dll",
            EntryPoint = "VBVMR_GetParameterStringW",
            CallingConvention = CallingConvention.StdCall,
            CharSet = CharSet.Unicode)]
        static extern int GetParamStringW64(
            [MarshalAs(UnmanagedType.LPStr)] string szParamName,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder szString);

        public static bool TryGetVoicemeeterType(out int type)
        {
            type = 0;

            try
            {
                int result = Environment.Is64BitProcess
                    ? GetType64(ref type)
                    : GetType32(ref type);

                return result == 0;
            }
            catch
            {
                type = 0;
                return false;
            }
        }

        public static string GetVoicemeeterKindName()
        {
            if (!TryGetVoicemeeterType(out int type))
                return "неизвестно";

            return type switch
            {
                1 => "Basic",
                2 => "Banana",
                3 => "Potato",
                6 => "Potato x64",
                _ => $"неизвестно (код {type})"
            };
        }

        public static string TryGetLabel(string paramName)
        {
            var sb = new StringBuilder(512);

            try
            {
                int result = Environment.Is64BitProcess
                    ? GetParamStringW64(paramName, sb)
                    : GetParamStringW32(paramName, sb);

                return result == 0 ? sb.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        public class ChannelInfo
        {
            public string Kind { get; set; } = "";
            public int Index { get; set; }
            public string Label { get; set; } = "";
            public string ParamPrefix =>
                $"{Kind}[{Index}]";
        }

        public static List<ChannelInfo> DiscoverChannels()
        {
            var result = new List<ChannelInfo>();

            if (!TryGetVoicemeeterType(out int type))
                return result;

            int stripCount = type switch
            {
                1 => 3,
                2 => 5,
                3 => 8,
                6 => 8,
                _ => 0
            };

            int busCount = type switch
            {
                1 => 2,
                2 => 5,
                3 => 8,
                6 => 8,
                _ => 0
            };

            for (int i = 0; i < stripCount; i++)
            {
                string label =
                    TryGetLabel($"Strip[{i}].Label");

                if (label == null)
                    label = $"Strip {i + 1}";

                result.Add(new ChannelInfo
                {
                    Kind = "Strip",
                    Index = i,
                    Label = string.IsNullOrWhiteSpace(label)
                        ? $"Strip {i + 1}"
                        : label
                });
            }

            for (int i = 0; i < busCount; i++)
            {
                string label =
                    TryGetLabel($"Bus[{i}].Label");

                if (label == null)
                    label = $"Bus {i + 1}";

                result.Add(new ChannelInfo
                {
                    Kind = "Bus",
                    Index = i,
                    Label = string.IsNullOrWhiteSpace(label)
                        ? $"Bus {i + 1}"
                        : label
                });
            }

            return result;
        }
    }
}
