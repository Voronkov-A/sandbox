using System.Text.Json;
using Picshare.Models;

namespace Picshare.Services;

public static class AiDuplicateFeedbackMerger
{
    /// <summary>Builds a complete update without mutating live feedback before it can be saved.</summary>
    public static ReviewerFeedbackDatabase Merge(ReviewerFeedbackDatabase database,
        IReadOnlyList<IReadOnlyList<string>> groups, IReadOnlySet<string> allowedPhotoIds)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (group.Count < 2 || group.Any(id => !allowedPhotoIds.Contains(id) || database.FrozenPhotoIds.Contains(id) || !used.Add(id)))
                throw new InvalidDataException("AI groups contain invalid, frozen or overlapping photos.");
            var ids = group.ToHashSet(StringComparer.Ordinal);
            if (database.DuplicateGroups.Any(existing => existing.PhotoIds.Any(ids.Contains) && existing.PhotoIds.Any(id => !ids.Contains(id))))
                throw new InvalidDataException("AI groups must preserve all members of existing duplicate groups.");
        }

        var updated = JsonSerializer.Deserialize<ReviewerFeedbackDatabase>(JsonSerializer.Serialize(database))!;
        foreach (var group in groups)
        {
            var ids = group.ToHashSet(StringComparer.Ordinal);
            var bestIds = updated.DuplicateGroups.Where(existing => existing.PhotoIds.Any(ids.Contains))
                .Select(existing => existing.BestPhotoId).Where(id => !string.IsNullOrEmpty(id) && ids.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
            updated.DuplicateGroups.RemoveAll(existing => existing.PhotoIds.Any(ids.Contains));
            updated.DuplicateGroups.Add(new DuplicatePhotoGroup
            {
                PhotoIds = group.ToList(), BestPhotoId = bestIds.Count == 1 ? bestIds[0] : ""
            });
            // Match manual duplicate marking: Nice, then Okay, then Trash, then Todo.
            var categories = group.Select(id => updated.PhotoCategories.GetValueOrDefault(id, "")).ToHashSet(StringComparer.Ordinal);
            var category = new[] { "nice", "ok", "trash" }.FirstOrDefault(categories.Contains) ?? "";
            foreach (var id in group)
            {
                if (category.Length == 0) updated.PhotoCategories.Remove(id);
                else updated.PhotoCategories[id] = category;
            }
        }
        return updated;
    }
}
