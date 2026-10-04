using UnityEngine;

namespace ConquestusTerrarum.Client
{
    // Local MVP storage, not encrypted credential protection. Never print the record.
    public sealed class PlayerPrefsSessionRecordStore : ISessionRecordStore
    {
        private const string Key = "ConquestusTerrarum.AnonymousSession.v1";
        public bool HasRecord => PlayerPrefs.HasKey(Key);
        public string Read() => PlayerPrefs.GetString(Key, "");
        public void Write(string record)
        {
            PlayerPrefs.SetString(Key, record);
            PlayerPrefs.Save();
        }
        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
