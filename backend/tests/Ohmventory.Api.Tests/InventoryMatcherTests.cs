using Ohmventory.Api.Assistant;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Tests;

public class InventoryMatcherTests
{
    private static Part Part(string name, string? partNumber = null, params string[] tags) =>
        new() { Id = Guid.NewGuid(), Name = name, PartNumber = partNumber, Tags = [.. tags] };

    [Fact]
    public void PartNumberMatchesIgnoringCaseAndSeparators()
    {
        var part = Part("Temperatursensor", "DS18B20");

        Assert.Equal(1.0, InventoryMatcher.Score(part, "Temperaturfühler", "ds18-b20"));
    }

    [Fact]
    public void PartNumberInStoredNameMatches()
    {
        var part = Part("BME280 Sensor (Breakout)");

        Assert.Equal(1.0, InventoryMatcher.Score(part, "Umweltsensor", "BME280"));
    }

    [Fact]
    public void StoredPartNumberInSuggestedNameMatches()
    {
        var part = Part("Display", "SSD1306");

        Assert.Equal(0.95, InventoryMatcher.Score(part, "SSD1306 OLED 0.96 Zoll", null));
    }

    [Fact]
    public void UnrelatedPartsDoNotMatch()
    {
        var parts = new[] { Part("BME280 Sensor", "BME280"), Part("Arduino Nano", "ATmega328P") };

        Assert.Empty(InventoryMatcher.FindMatches(parts, "ESP32 DevKit", "ESP32"));
    }

    [Fact]
    public void ShortPartNumbersAreNotMatchedBySubstring()
    {
        var part = Part("LM7805 Spannungsregler", "LM7805");

        Assert.True(InventoryMatcher.Score(part, "Widerstand", "LM") < InventoryMatcher.Threshold);
    }

    [Fact]
    public void BestMatchComesFirst()
    {
        var exact = Part("ESP32 DevKit V1", "ESP32-WROOM-32");
        var similar = Part("ESP8266 DevKit", "ESP-12F");

        var matches = InventoryMatcher.FindMatches([similar, exact], "ESP32 DevKit", "ESP32-WROOM-32");

        Assert.Same(exact, matches[0].Part);
    }
}
