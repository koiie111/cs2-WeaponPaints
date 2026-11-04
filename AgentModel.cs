namespace WeaponPaints;

internal static class AgentModel
{
    internal static string? Normalize(string? model)
    {
        model = model?.Trim();
        return string.IsNullOrEmpty(model) || model == "0" ||
            model.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : model;
    }

    internal static string? ForTeam(string? model, int team)
    {
        model = Normalize(model);
        return IsAllowed(model, team) ? model : null;
    }

    internal static bool IsAllowed(string? model, int team) => model != null &&
        WeaponPaints.AgentsList.Any(agent =>
            (int?)agent["team"] == team && (string?)agent["model"] == model);
}
