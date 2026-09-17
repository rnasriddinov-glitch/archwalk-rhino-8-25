namespace ArchWalk.Core.Support;

/// <summary>
/// Local vertical support query in SI metres. Implementations must search only within
/// [referenceZ - downMeters, referenceZ + upMeters], never a whole-model top-down ray.
/// </summary>
public interface ISupportField
{
    SupportHit Probe(double xMeters, double yMeters, double referenceZMeters, double upMeters, double downMeters);
}
