using System;
using System.Text.Json;
using VoicemeeterSliderControl;

static class Program
{
    static int Main()
    {
        AssertClose(
            ArduinoConnection.Map(0, 0, 1023, 0, 100),
            0,
            0.0001f,
            "Map lower bound");

        AssertClose(
            ArduinoConnection.Map(1023, 0, 1023, 0, 100),
            100,
            0.0001f,
            "Map upper bound");

        var values =
            ArduinoConnection.ParseValuesFloat(
                "0|1023|512|garbage");

        if (values.Count != 3)
            throw new Exception(
                $"Parser returned {values.Count} values, expected 3.");

        AssertClose(
            values[0],
            0,
            0.0001f,
            "Parser lower bound");

        AssertClose(
            values[1],
            100,
            0.0001f,
            "Parser upper bound");

        var config = new AppConfig
        {
            PortName = "COM7",
            ShowOsd = true,
            SliderSets =
            {
                new SliderMapping
                {
                    Slider = 0,
                    VoicemeeterValue =
                        "Strip[0].Gain",
                    DisplayName =
                        "Microphone"
                }
            }
        };

        string json =
            JsonSerializer.Serialize(config);

        var roundTrip =
            JsonSerializer.Deserialize<AppConfig>(
                json);

        if (roundTrip == null ||
            roundTrip.PortName != "COM7" ||
            !roundTrip.ShowOsd ||
            roundTrip.SliderSets.Count != 1 ||
            roundTrip.SliderSets[0]
                .DisplayName != "Microphone")
        {
            throw new Exception(
                "Config JSON round-trip failed.");
        }

        Console.WriteLine(
            "Smoke tests passed.");

        return 0;
    }

    static void AssertClose(
        float actual,
        float expected,
        float tolerance,
        string name)
    {
        if (Math.Abs(actual - expected) >
            tolerance)
        {
            throw new Exception(
                $"{name}: actual={actual}, expected={expected}.");
        }
    }
}
