namespace NektoTranslate.Translation.Services;


// The reply did not come back in the segment format it was asked for. Recoverable by one retry;
// past that the chapter is left failed rather than written half-formed.
public class SegmentProtocolException(string message) : Exception(message);
