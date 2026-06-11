using InfluxDB.Client.Writes;
using Tts.Api.Models;

namespace Tts.Api.Services;

/// <summary>
/// Converts a generic <see cref="TransactionEventDto"/> into an InfluxDB <see cref="PointData"/>.
/// An interface so alternative mappings (different measurement/tag conventions) can be plugged in later.
/// </summary>
public interface IEventTransformer
{
    PointData Transform(TransactionEventDto dto);
}
