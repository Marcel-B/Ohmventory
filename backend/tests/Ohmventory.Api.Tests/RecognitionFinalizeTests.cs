using Ohmventory.Api.Domain;
using Ohmventory.Api.Recognition;

namespace Ohmventory.Api.Tests;

public class RecognitionFinalizeTests
{
    private static VisionAnalysis Analysis(string? partNumber, double confidence, params string[] visibleText) =>
        new([.. visibleText], "Rückseite, Flachbandkabel", "Teil", PartCategory.Display, null, partNumber, null, null, true, null, confidence);

    [Fact]
    public void DropsReferenceDesignatorsButKeepsModuleAndPinLabels()
    {
        var result = PartRecognitionService.Finalize(Analysis(null, 0.5, "C1", "R12", "D1", "U2", "1.30\" IIC V2.2", "VDD", "GND", "SCK", "SDA"));

        Assert.Equal(["1.30\" IIC V2.2", "VDD", "GND", "SCK", "SDA"], result.MarkingsRead);
    }

    [Fact]
    public void CapsConfidenceWhenPartNumberWasNotRead()
    {
        // Der beobachtete Fehler: OLED-Rückseite als ESP32 mit 95 %.
        var result = PartRecognitionService.Finalize(Analysis("ESP32", 0.95, "C1", "VDD", "GND"));

        Assert.Equal(PartRecognitionService.MaxConfidenceWithoutPartNumber, result.Confidence);
    }

    [Fact]
    public void KeepsHighConfidenceWhenPartNumberIsPrinted()
    {
        var result = PartRecognitionService.Finalize(Analysis("BME280", 0.95, "GY-BME280", "VIN", "GND"));

        Assert.Equal(0.95, result.Confidence);
    }

    [Fact]
    public void ToleratesMissingListsFromTheModel()
    {
        var analysis = new VisionAnalysis(null, null, "Teil", PartCategory.Other, null, "  ", null, null, false, null, 1.4);

        var result = PartRecognitionService.Finalize(analysis);

        Assert.Empty(result.MarkingsRead);
        Assert.Empty(result.Alternatives);
        Assert.Null(result.PartNumber);
        Assert.Equal(PartRecognitionService.MaxConfidenceWithoutPartNumber, result.Confidence);
    }
}
