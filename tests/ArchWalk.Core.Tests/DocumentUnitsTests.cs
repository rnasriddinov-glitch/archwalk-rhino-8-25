using ArchWalk.Core.Units;
using ArchWalk.Core.Motion;
using Xunit;

namespace ArchWalk.Core.Tests;

public sealed class DocumentUnitsTests
{
    [Fact]
    public void Millimetres_EyeHeightAndWalkMatchSpecTable()
    {
        var units = DocumentUnits.Millimeters;
        Assert.Equal(1550, units.ToDocument(MotionDefaults.EyeHeightMeters), 9);
        Assert.Equal(1300, units.ToDocument(MotionDefaults.BaseSpeedMetersPerSecond), 9);
    }

    [Fact]
    public void Metres_EyeHeightAndWalkMatchSpecTable()
    {
        var units = DocumentUnits.Meters;
        Assert.Equal(1.55, units.ToDocument(MotionDefaults.EyeHeightMeters), 9);
        Assert.Equal(1.3, units.ToDocument(MotionDefaults.BaseSpeedMetersPerSecond), 9);
    }

    [Fact]
    public void InternationalFeet_EyeHeightAndWalkMatchSpecTable()
    {
        var units = DocumentUnits.InternationalFeet;
        Assert.Equal(5.08530184, units.ToDocument(MotionDefaults.EyeHeightMeters), 6);
        Assert.Equal(4.26509186, units.ToDocument(MotionDefaults.BaseSpeedMetersPerSecond), 6);
    }

    [Fact]
    public void RoundTrip_PreservesMetres()
    {
        var units = DocumentUnits.Millimeters;
        Assert.Equal(1.55, units.ToMeters(units.ToDocument(1.55)), 12);
    }
}
