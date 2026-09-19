namespace Parrot.Core.Data;

internal static class Schema
{
    public const string V1 = """
        CREATE TABLE Deck (
            Id         INTEGER PRIMARY KEY AUTOINCREMENT,
            Name       TEXT    NOT NULL,
            FrontLang  TEXT    NOT NULL DEFAULT 'en',
            BackLang   TEXT    NOT NULL DEFAULT 'uk',
            IsActive   INTEGER NOT NULL DEFAULT 1,
            CreatedAt  TEXT    NOT NULL
        );

        CREATE TABLE Card (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            DeckId      INTEGER NOT NULL REFERENCES Deck(Id) ON DELETE CASCADE,
            Front       TEXT    NOT NULL,
            Back        TEXT    NOT NULL,
            Hint        TEXT,
            Example     TEXT,
            Tags        TEXT,
            Notes       TEXT,
            IsSuspended INTEGER NOT NULL DEFAULT 0,
            CreatedAt   TEXT    NOT NULL,
            UpdatedAt   TEXT    NOT NULL,
            DeletedAt   TEXT
        );

        CREATE TABLE CardSchedule (
            CardId             INTEGER PRIMARY KEY REFERENCES Card(Id) ON DELETE CASCADE,
            EaseFactor         REAL    NOT NULL,
            IntervalMinutes    REAL    NOT NULL,
            Repetitions        INTEGER NOT NULL,
            Lapses             INTEGER NOT NULL,
            DueAt              TEXT    NOT NULL,
            LastShownAt        TEXT,
            CorrectCount       INTEGER NOT NULL,
            WrongCount         INTEGER NOT NULL,
            IgnoredCount       INTEGER NOT NULL,
            ConsecutiveIgnores INTEGER NOT NULL,
            Streak             INTEGER NOT NULL
        );

        CREATE TABLE ReviewLog (
            Id         INTEGER PRIMARY KEY AUTOINCREMENT,
            CardId     INTEGER NOT NULL REFERENCES Card(Id) ON DELETE CASCADE,
            ShownAt    TEXT    NOT NULL,
            AnsweredAt TEXT,
            Outcome    INTEGER NOT NULL,
            UserAnswer TEXT,
            Direction  INTEGER NOT NULL,
            ResponseMs INTEGER NOT NULL
        );

        CREATE INDEX IX_Card_DeckId    ON Card(DeckId);
        CREATE INDEX IX_Card_DeletedAt ON Card(DeletedAt);
        CREATE INDEX IX_Schedule_DueAt ON CardSchedule(DueAt);
        CREATE INDEX IX_Review_ShownAt ON ReviewLog(ShownAt);
        CREATE INDEX IX_Review_CardId  ON ReviewLog(CardId);
        """;
}
