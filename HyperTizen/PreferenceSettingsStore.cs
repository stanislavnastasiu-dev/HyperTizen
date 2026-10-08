using HyperTizen.Core;
using Tizen.Applications;

namespace HyperTizen
{
    public class PreferenceSettingsStore : ISettingsStore
    {
        public bool Contains(string key)
        {
            return Preference.Contains(key);
        }

        public string Get(string key)
        {
            return Preference.Get<string>(key);
        }

        public void Set(string key, string value)
        {
            Preference.Set(key, value);
        }

        public void Remove(string key)
        {
            if (Preference.Contains(key)) Preference.Remove(key);
        }
    }
}
