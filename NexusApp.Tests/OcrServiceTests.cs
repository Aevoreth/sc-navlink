using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

// OcrService.ExtractRsValue (Services/OcrService.cs) is the RS Decoder's digit parser: comma/period
// stripping between digits, a regex collapse for OCR-misread thousands separators, then a bounded
// digit-run scan. OcrService.cs preprocessing is locked by project rule; the ONLY permitted edit here (per
// the 2026-07-28 app-wide review spec's seam ruling) was widening ExtractRsValue's access modifier
// from private to internal, method body byte-identical, so it is reachable via the existing
// InternalsVisibleTo("NexusApp.Tests") entry in NexusApp.csproj. These tests PIN the method's current
// shipped behavior - they assert what it does, not what it "should" do.
public class OcrServiceTests
{
    [Theory]
    // -- comma/period stripping between digits ("17,200"/"17.200" -> "17200") --
    [InlineData("RS DECODER 17,200 CREDITS", 17200)]
    [InlineData("RS DECODER 17.200 CREDITS", 17200)]
    // -- thousands-separator regex collapse: 1-3 leading digits + space + exactly 3 digits --
    [InlineData("value 5 000 detected", 5000)]
    [InlineData("value 17 200 detected", 17200)]
    [InlineData("value 18 500 detected", 18500)]
    // -- 2000-200000 bounds: acceptance at both ends --
    [InlineData("value 2000 detected", 2000)]
    [InlineData("value 200000 detected", 200000)]
    // -- 2000-200000 bounds: rejection just outside both ends --
    [InlineData("value 1999 detected", null)]
    [InlineData("value 200001 detected", null)]
    // -- no digits at all --
    [InlineData("no value found here", null)]
    // -- multiple digit runs: a too-short/out-of-range run is skipped, scan continues to the next --
    [InlineData("12 45000", 45000)]
    [InlineData("1500 8000", 8000)]
    public void ExtractRsValue_PinsCurrentBehavior(string ocrText, int? expected)
    {
        Assert.Equal(expected, OcrService.ExtractRsValue(ocrText));
    }

    [Fact]
    public void ExtractRsValues_OneSignature_MatchesExtractRsValue()
    {
        var text = "RS DECODER 17,200 CREDITS";
        Assert.Equal(new[] { 17200 }, OcrService.ExtractRsValues(text));
        Assert.Equal(17200, OcrService.ExtractRsValue(text));
    }

    [Fact]
    public void ExtractRsValues_TwoSignaturesOnOneLine_AreDistinct()
    {
        Assert.Equal(new[] { 17200, 45000 }, OcrService.ExtractRsValues("17200 45000"));
    }

    [Fact]
    public void ExtractRsValues_ThreeSignaturesOnSeparateLines_KeepOrder()
    {
        var text = "17200\n45000\n8000";
        Assert.Equal(new[] { 17200, 45000, 8000 }, OcrService.ExtractRsValues(text));
        Assert.Equal(17200, OcrService.ExtractRsValue(text));
    }

    [Fact]
    public void ExtractRsValues_DuplicateOnSameFrame_IsOnce()
    {
        Assert.Equal(new[] { 17200 }, OcrService.ExtractRsValues("17200\n17200"));
    }

    [Fact]
    public void ExtractRsValues_ThreeConcatenatedSignatures_AreSplit()
    {
        Assert.Equal(new[] { 17200, 45000, 8000 }, OcrService.ExtractRsValues("17200450008000"));
        Assert.Equal(17200, OcrService.ExtractRsValue("17200450008000"));
    }

    [Fact]
    public void ExtractRsValues_TwoConcatenatedSignatures_AreSplit()
    {
        Assert.Equal(new[] { 17200, 45000 }, OcrService.ExtractRsValues("1720045000"));
    }

    [Fact]
    public void ExtractRsValues_HudCommaStack_DropsSubFloorAndKeepsValid()
    {
        // In-game stacked scan pills: 5,291 / 18,500 / 1,328. 1328 is under the 2000 floor
        // and must not wipe the other two, whether OCR keeps commas, turns them into spaces,
        // or concatenates the three into one digit run.
        Assert.Equal(new[] { 5291, 18500 }, OcrService.ExtractRsValues("5,291\n18,500\n1,328"));
        Assert.Equal(new[] { 5291, 18500 }, OcrService.ExtractRsValues("5 291 18 500 1 328"));
        Assert.Equal(new[] { 5291, 18500 }, OcrService.ExtractRsValues("5291185001328"));
        Assert.Equal(new[] { 5291, 18500 }, OcrService.SplitLongDigitRun("5291185001328"));
    }
}
