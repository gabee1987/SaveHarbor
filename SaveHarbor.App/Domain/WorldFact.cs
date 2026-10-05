namespace SaveHarbor.App.Domain;

public enum WorldFactGroup
{
    Details,
    Rules
}

public sealed record WorldFact(WorldFactGroup Group, string Label, string Value);
