namespace NektoTranslate.Jobs.Contracts;


// One unit of a run's work just done or just begun, reported by whoever does the work to whoever
// shows it. `title` is the sentence for the strip; `index`/`count` place it inside the current
// chapter (or, for learning, inside the whole run); `costUsd` is what this step cost, 0 when it is
// only an announcement.
public sealed record RunStep(string title, int index, int count, double costUsd = 0);
