using System.Collections.Generic;

namespace VoicemeeterSliderControl
{
    public class SliderMapping
    {
        public int Slider { get; set; }
        public string VoicemeeterValue { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }

    public class AppConfig
    {
        public string PortName { get; set; } = "";
        public int BaudRate { get; set; } = 115200;
        public bool ShowOsd { get; set; } = true;
        public List<SliderMapping> SliderSets { get; set; } = new List<SliderMapping>();
    }
}
