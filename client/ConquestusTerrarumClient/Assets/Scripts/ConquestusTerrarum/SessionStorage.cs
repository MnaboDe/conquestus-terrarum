#nullable disable
using System;

namespace ConquestusTerrarum.Client
{
    public interface ISessionStorage
    {
        bool HasSavedSession { get; }
        MultiplayerSession Load();
        void Save(MultiplayerSession session);
        void Clear();
    }

    // A single record keeps partial identities from being mixed. Platform-secure stores
    // can replace ISessionStorage entirely; this adapter also makes the MVP codec testable.
    public interface ISessionRecordStore
    {
        bool HasRecord { get; }
        string Read();
        void Write(string record);
        void Clear();
    }

    public sealed class SessionStorage : ISessionStorage
    {
        private readonly ISessionRecordStore _records;
        public SessionStorage(ISessionRecordStore records) => _records = records ?? throw new ArgumentNullException(nameof(records));
        // Corrupt records remain visible so the user must explicitly Forget before replacement.
        public bool HasSavedSession => _records.HasRecord;

        public MultiplayerSession Load()
        {
            if (!HasSavedSession) return null;
            var record = _records.Read();
            if (record == null || record.Length > 256) return null;
            var fields = record.Split('\n');
            if (fields.Length != 5 || fields[0] != "1") return null;
            return MultiplayerSession.Restore(fields[1], fields[2], fields[3], fields[4]);
        }

        public void Save(MultiplayerSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var record = string.Join("\n", "1", session.MatchId, session.PlayerId, session.PlayerToken, session.JoinCode);
            // Allow retrying the same save after a flush failure, never replacing another record.
            if (HasSavedSession && _records.Read() != record)
                throw new InvalidOperationException("Forget the saved game before saving another session.");
            _records.Write(record);
        }

        public void Clear() => _records.Clear();
    }
}
