using ArchWalk.Core.Math;
using ArchWalk.Core.Support;

namespace ArchWalk.Core.Support;

/// <summary>Multiple candidate Z levels; picks the one nearest to the reference inside the local window.</summary>
public sealed class MultiLevelSupportField : ISupportField
{
    readonly double[] _levels;
    readonly Func<double, double, bool>? _mask;

    public MultiLevelSupportField(IEnumerable<double> levelsMeters, Func<double, double, bool>? mask = null)
    {
        _levels = levelsMeters.OrderBy(z => z).ToArray();
        _mask = mask;
    }

    public SupportHit Probe(double xMeters, double yMeters, double referenceZMeters, double upMeters, double downMeters)
    {
        if (_mask is not null && !_mask(xMeters, yMeters))
            return SupportHit.Miss;

        var best = SupportHit.Miss;
        var bestDelta = double.PositiveInfinity;
        foreach (var z in _levels)
        {
            if (z > referenceZMeters + upMeters + 1e-9)
                continue;
            if (z < referenceZMeters - downMeters - 1e-9)
                continue;
            var delta = System.Math.Abs(z - referenceZMeters);
            if (delta >= bestDelta)
                continue;
            bestDelta = delta;
            best = SupportHit.At(z, Vec3.UnitZ);
        }

        return best;
    }
}
