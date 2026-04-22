using System.Collections.Generic;
using Behaviour.Equipment.Builder;       // ManufacturerExtensions.GetFaction
using Source.Galaxy.POI.Station;
using Source.Galaxy.POI.Station.Patrons;

namespace VGAnima.Llm;

/// <summary>Projects the current bar's non-broker patrons into an
/// <see cref="LlmBarEcosystemSection"/> so a VGAnima broker can reference
/// "the Prospector over there" or "the blueprint dealer at the next
/// table." All signals are derived live from
/// <see cref="Bar.availablePatrons"/> — no hooks, no persistence.
///
/// <para>Filters: skip VGAnima-injected salesmen (their own history is
/// covered by the journal layer). Classify each remaining
/// <see cref="Salesman"/> via <c>itemForSale.itemBuilder.identifier</c>
/// (canonical strings: <c>MiningClaim</c>, <c>SalvageClaim</c>,
/// <c>SpaceShipPng</c>, else equipment-builder id → "Equipment Rep").</para>
///
/// <para>Crew-member patrons (<see cref="CrewMember"/>) are also surfaced
/// as a simple "crew-for-hire" signal. The salesman-type distribution
/// over a typical bar is ~30% CrewMember, ~35% Equipment Rep,
/// ~14% Prospector, ~14% Salvage Scout, ~7% Slick Entrepreneur per the
/// bar-ecosystem survey §1 — usually 1-3 non-broker patrons per bar.</para></summary>
internal static class BarEcosystemBuilder
{
    /// <summary>Classification kinds — stable strings for prompt rule
    /// matching. Mirror the survey's canonical taxonomy.</summary>
    internal static class Kinds
    {
        public const string Prospector      = "Prospector";
        public const string SalvageScout    = "Salvage Scout";
        public const string SlickEntrepreneur = "Slick Entrepreneur";
        public const string EquipmentRep    = "Equipment Rep";
        public const string CrewRecruiter   = "Crew Recruiter";
    }

    /// <summary>Builds the section from a live <see cref="Bar"/>.
    /// <paramref name="vganimaBrokerSeeds"/> is a set of seeds for
    /// VGAnima-injected salesmen at this bar — filtered out so brokers
    /// don't see themselves in their own context. Accepts null for ease
    /// of testing.</summary>
    public static LlmBarEcosystemSection Build(
        Bar? bar, ISet<string>? vganimaBrokerSeeds = null)
    {
        if (bar is null)
            return Empty;

        var entries = new List<LlmBarSalesmanEntry>();
        foreach (var patron in bar.availablePatrons)
        {
            // Two classes at the bar: Salesman (procedural economic
            // patrons) and CrewMember (crew-for-hire). Skip anything
            // else defensively — future vanilla additions shouldn't
            // crash our context build.
            switch (patron)
            {
                case Salesman s:
                    if (vganimaBrokerSeeds != null && vganimaBrokerSeeds.Contains(s.seed))
                        continue;
                    entries.Add(ClassifySalesman(s));
                    break;
                case CrewMember cm:
                    entries.Add(new LlmBarSalesmanEntry(
                        Kind: Kinds.CrewRecruiter,
                        Name: cm.name ?? "a crew recruiter",
                        ItemIdentifier: null,
                        Faction: null));
                    break;
            }
        }

        return new LlmBarEcosystemSection { OtherSalesmenHere = entries };
    }

    private static LlmBarSalesmanEntry ClassifySalesman(Salesman s)
    {
        var id = s.itemForSale?.itemBuilder?.identifier ?? "?";
        var kind = id switch
        {
            "MiningClaim"  => Kinds.Prospector,
            "SalvageClaim" => Kinds.SalvageScout,
            "SpaceShipPng" => Kinds.SlickEntrepreneur,
            _              => Kinds.EquipmentRep,
        };

        // Equipment Rep is the only kind that carries a manufacturer /
        // faction signal. GetManufacturer is implemented on the item
        // type; GetFaction() returns null for generic manufacturers.
        // 6 of 15 manufacturers map to a faction per survey §1.
        string? faction = null;
        if (kind == Kinds.EquipmentRep && s.itemForSale is { } item)
        {
            try { faction = item.GetManufacturer()?.GetFaction()?.identifier; }
            catch { /* defensive — older saves / partial state */ }
        }

        return new LlmBarSalesmanEntry(
            Kind:           kind,
            Name:           s.name ?? "a salesman",
            ItemIdentifier: id == "?" ? null : id,
            Faction:        faction);
    }

    private static readonly LlmBarEcosystemSection Empty = new()
    {
        OtherSalesmenHere = System.Array.Empty<LlmBarSalesmanEntry>(),
    };
}
