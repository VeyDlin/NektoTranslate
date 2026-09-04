using NektoTranslate.Translation.Services;
using Xunit;


namespace NektoTranslate.Tests.Translation;


// Shifting an imported translation onto the right chapters.
//
// The case that drives all of this: a translation site opens with a translator's note, so its first
// entry is not chapter one and everything after it is one place out. Correcting that means moving a
// contiguous block by one - onto chapters that are occupied at the moment the move is planned, by
// the very translations that are moving.
public class TranslationMovePlanTests {

    private static HashSet<int> Chapters(int count) {
        return Enumerable.Range(0, count).ToHashSet();
    }


    [Fact]
    public void AShiftIntoEmptyChaptersIsPlanned() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(10),
            occupied: [0, 1, 2],
            fromIndex: 0,
            toIndex: 2,
            offset: 5
        );

        Assert.Empty(plan.collisions);
        Assert.Equal([(0, 5), (1, 6), (2, 7)], plan.moves);
    }


    // The translator's-note correction, and the reason the planner exists. Chapters 1-19 hold the
    // translation, chapter 0 holds nothing because the note was deleted, and the block slides up by
    // one. Every target except the last is occupied right now - by a translation that is itself
    // moving out of the way.
    [Fact]
    public void ABlockSlidingOverItsOwnDestinationsIsNotACollision() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(30),
            occupied: [.. Enumerable.Range(1, 19)],
            fromIndex: 1,
            toIndex: 19,
            offset: -1
        );

        Assert.Empty(plan.collisions);
        Assert.Equal(19, plan.moves.Count);
        Assert.Equal((1, 0), plan.moves[0]);
        Assert.Equal((19, 18), plan.moves[^1]);
    }


    // The same overlap in the other direction, which a rule written only for upward moves would get
    // wrong.
    [Fact]
    public void TheOverlapRuleHoldsShiftingDownAsWell() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(30),
            occupied: [.. Enumerable.Range(0, 19)],
            fromIndex: 0,
            toIndex: 18,
            offset: 1
        );

        Assert.Empty(plan.collisions);
        Assert.Equal(19, plan.moves.Count);
    }


    [Fact]
    public void AStationaryOccupantIsACollisionAndNothingIsPlanned() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(10),
            // 5 holds a translation and is not in the range being moved.
            occupied: [0, 1, 5],
            fromIndex: 0,
            toIndex: 1,
            offset: 5
        );

        Assert.Single(plan.collisions);
        Assert.Equal(0, plan.collisions[0].fromIndex);
        Assert.Equal(5, plan.collisions[0].targetIndex);

        // The other chapter in the range did have a clear target, and the planner still reports it.
        // The planner describes; refusing is the service's job, and it refuses whenever any
        // collision is present. Keeping the workable moves visible is what lets the interface show
        // "these would move, this one is blocked" instead of just failing.
        Assert.Single(plan.moves);
    }


    [Fact]
    public void MovingPastTheEndOfTheBookIsACollisionRatherThanASilentDrop() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(5),
            occupied: [3, 4],
            fromIndex: 3,
            toIndex: 4,
            offset: 2
        );

        Assert.Equal(2, plan.collisions.Count);
        Assert.All(plan.collisions, collision => Assert.Equal("MOVE_TARGET_MISSING", collision.reason.code));
    }


    // Chapters in the range that hold nothing are simply not part of the move - a gap in the
    // imported translation must not drag its neighbours along to close it.
    [Fact]
    public void OnlyChaptersThatHoldATranslationMove() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(10),
            occupied: [0, 3],
            fromIndex: 0,
            toIndex: 5,
            offset: 4
        );

        Assert.Empty(plan.collisions);
        Assert.Equal([(0, 4), (3, 7)], plan.moves);
    }


    [Fact]
    public void AZeroOffsetDoesNothing() {
        TranslationMovePlan.Plan plan = TranslationMovePlan.Build(
            Chapters(10),
            occupied: [0, 1, 2],
            fromIndex: 0,
            toIndex: 9,
            offset: 0
        );

        Assert.Empty(plan.moves);
        Assert.Empty(plan.collisions);
    }
}
