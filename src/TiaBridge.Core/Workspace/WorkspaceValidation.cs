namespace TiaBridge.Core.Workspace;

public static class WorkspaceValidation
{
    internal static void Validate(WorkspaceManifest manifest)
    {
        if (manifest.ProjectKey != manifest.Project.ProjectKey) Fail();
        var project = manifest.Project;
        var devices = project.Devices.ToDictionary(d => d.DeviceId);
        var groups = project.Groups.ToDictionary(g => g.GroupId);
        var objects = manifest.Inventory.ToDictionary(o => o.ObjectId);
        var units = manifest.ExchangeUnits.ToDictionary(u => u.UnitId, StringComparer.Ordinal);
        if (devices.Keys.Concat(groups.Keys).Concat(objects.Keys).Distinct().Count()
            != devices.Count + groups.Count + objects.Count) Fail();
        foreach (var group in project.Groups)
        {
            if (!devices.ContainsKey(group.DeviceId)) Fail();
            var seen = new HashSet<Identity.ObjectId> { group.GroupId };
            var current = group;
            while (current.ParentGroupId is not null)
            {
                if (!groups.TryGetValue(current.ParentGroupId, out var parent) || parent.DeviceId != group.DeviceId
                    || !seen.Add(parent.GroupId)) Fail();
                current = groups[current.ParentGroupId];
            }
        }
        foreach (var item in manifest.Inventory)
        {
            var locator = item.VendorLocator;
            if (!devices.ContainsKey(locator.DeviceId) || locator.Kind != item.Kind
                || (locator.PublicId is null) != (locator.PublicIdValidityScope is null)) Fail();
            Identity.ObjectId? previous = null;
            foreach (var id in locator.Groups)
            {
                if (!groups.TryGetValue(id, out var group) || group.DeviceId != locator.DeviceId || group.ParentGroupId != previous) Fail();
                previous = id;
            }
            if (locator.Groups.Distinct().Count() != locator.Groups.Count) Fail();
            foreach (var relation in item.Dependencies)
                if (!objects.ContainsKey(relation.TargetObjectId)) Fail();
            ValidateCoverage(item.Coverage);
            if (item.Status == InventoryStatus.REPRESENTED)
            {
                if (item.ExchangeUnitId is null || !units.TryGetValue(item.ExchangeUnitId, out var unit)
                    || !unit.Members.Contains(item.ObjectId) || item.FilePaths.Count == 0 || item.ModelSha256 is null) Fail();
            }
            else if (string.IsNullOrWhiteSpace(item.Reason)) Fail();
            if (item.ExchangeUnitId is not null)
            {
                if (!units.TryGetValue(item.ExchangeUnitId, out var unit) || !unit.Members.Contains(item.ObjectId)
                    || !item.FilePaths.Order(StringComparer.Ordinal).SequenceEqual(unit.Files.Select(f => f.RelativePath).Order(StringComparer.Ordinal))) Fail();
            }
            else if (item.FilePaths.Count != 0) Fail();
            WorkspacePath.ValidateUnique(item.FilePaths);
        }
        foreach (var unit in manifest.ExchangeUnits)
        {
            if (unit.Members.Distinct().Count() != unit.Members.Count || !unit.Files.Any(f => f.Role == RepresentationRole.NATIVE)) Fail();
            foreach (var id in unit.Members)
                if (!objects.TryGetValue(id, out var item) || item.ExchangeUnitId != unit.UnitId) Fail();
            foreach (var file in unit.Files)
                if (!(file.Role == RepresentationRole.NATIVE && file.RelativePath.StartsWith("artifacts/", StringComparison.Ordinal))
                    && !unit.Members.Any(id => file.RelativePath.StartsWith(WorkspaceLayout.ObjectDirectory(project, objects[id]) + "/", StringComparison.Ordinal))) Fail();
            if (WorkspaceHashing.Unit(unit, manifest.Inventory) != unit.UnitSha256) Fail();
        }
        WorkspacePath.ValidateUnique(manifest.ExchangeUnits.SelectMany(u => u.Files.Select(f => f.RelativePath)));
        // Detect suffix collisions even for inventory rows without files. Never choose a heuristic identity.
        var directories = manifest.Inventory.Select(o => WorkspaceLayout.ObjectDirectory(project, o)).ToArray();
        WorkspacePath.ValidateUnique(directories);
        // Directory nodes may contain one another, so use a synthetic leaf for collision checking.
        WorkspacePath.ValidateUnique(project.Devices.Select(WorkspaceLayout.DeviceDirectory)
            .Concat(project.Groups.Select(g => WorkspaceLayout.GroupDirectory(project, g)))
            .Concat(directories).Select(p => p + "/object.json"));
        ValidateCoverage(manifest.Coverage);
    }

    internal static void ValidateCoverage(Coverage coverage)
    {
        if (coverage.Dimensions.Select(d => d.Dimension).Distinct().Count() != coverage.Dimensions.Count) Fail();
        foreach (var dimension in coverage.Dimensions)
            if (dimension.State != CoverageState.COMPLETE && string.IsNullOrWhiteSpace(dimension.Reason)) Fail();
    }

    private static void Fail() => throw new ArgumentException("Invalid workspace model.");
}
