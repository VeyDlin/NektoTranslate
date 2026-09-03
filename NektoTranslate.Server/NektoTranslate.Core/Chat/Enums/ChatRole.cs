namespace NektoTranslate.Chat.Enums;


public enum ChatRole {
    User = 0,
    Agent = 1,
    // A tool the agent ran, kept in the transcript so the user can see what it did rather than only
    // what it said.
    Tool = 2
}
