using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Recovery identity for a presentation reserved before narrative assignment commits.</summary>
internal sealed record BrokerReservation(
    [property: JsonProperty("seed")] string Seed,
    [property: JsonProperty("stationId")] string StationId,
    [property: JsonProperty("aborted")] bool Aborted = false);
