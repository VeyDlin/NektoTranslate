using NektoTranslate.Translation.Contracts;


namespace NektoTranslate.Translation.Services;


// Works out which translations a shift would move and what it would run into, in chapter-index
// space and without touching the database.
//
// Pulled out of the mapping service because of one case that is easy to get wrong and impossible to
// notice: a contiguous block shifted by a small offset overlaps its own destinations. Moving the
// translations of chapters 1-19 up by one targets 0-18, and 1-18 are occupied *right now* by
// translations that are themselves moving out of the way. A naive occupancy check calls that a
// collision and refuses the single most common correction there is - the one that absorbs a
// translator's note sitting at the top of the list.
public static class TranslationMovePlan {

    public sealed record Plan(
        IReadOnlyList<(int source, int target)> moves,
        IReadOnlyList<TranslationCollision> collisions
    );


    // Sets rather than lists, and that is a requirement rather than a preference: the occupancy test
    // runs once per moved chapter, so a linear lookup would make a shift of a long book quadratic.
    public static Plan Build(
        HashSet<int> existingChapters,
        HashSet<int> occupied,
        int fromIndex,
        int toIndex,
        int offset
    ) {
        List<(int source, int target)> moves = [];
        List<TranslationCollision> collisions = [];

        if (offset == 0) {
            return new Plan(moves, collisions);
        }

        HashSet<int> moving = occupied
            .Where(index => index >= fromIndex && index <= toIndex)
            .ToHashSet();

        foreach (int source in moving.OrderBy(index => index)) {
            int target = source + offset;

            if (!existingChapters.Contains(target)) {
                collisions.Add(new TranslationCollision(
                    source,
                    target,
                    "There is no chapter at that position."
                ));

                continue;
            }

            // Occupied is only a conflict when the occupant is staying put. This is the whole reason
            // the planner exists.
            if (occupied.Contains(target) && !moving.Contains(target)) {
                collisions.Add(new TranslationCollision(
                    source,
                    target,
                    "That chapter already has a translation that is not part of this move."
                ));

                continue;
            }

            moves.Add((source, target));
        }

        return new Plan(moves, collisions);
    }
}
