namespace ArchWalk.Core.Units;

/// <summary>
/// Physical scale of one document unit. Core stores motion in SI metres.
/// </summary>
public readonly record struct DocumentUnits(double MetersPerDocumentUnit)
{
    public static DocumentUnits Millimeters { get; } = new(0.001);
    public static DocumentUnits Meters { get; } = new(1.0);
    public static DocumentUnits InternationalFeet { get; } = new(0.3048);

    public bool IsValid => MetersPerDocumentUnit > 0 && !double.IsNaN(MetersPerDocumentUnit) && !double.IsInfinity(MetersPerDocumentUnit);

    public double ToDocument(double meters) => meters / MetersPerDocumentUnit;

    public double ToMeters(double documentUnits) => documentUnits * MetersPerDocumentUnit;

    public static DocumentUnits FromMetersPerUnit(double metersPerDocumentUnit)
    {
        if (metersPerDocumentUnit <= 0 || double.IsNaN(metersPerDocumentUnit) || double.IsInfinity(metersPerDocumentUnit))
            throw new ArgumentOutOfRangeException(nameof(metersPerDocumentUnit), "Physical scale must be a positive finite number.");
        return new DocumentUnits(metersPerDocumentUnit);
    }
}
