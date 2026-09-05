namespace NektoTranslate.Parsing.Enums;


public enum ListingState {

    // The site is being read right now. Persisted rather than held in memory so a screen opened
    // during the read finds the read, and so a second press cannot start a second one.
    Reading = 0,

    Ready = 1,

    Failed = 2
}
