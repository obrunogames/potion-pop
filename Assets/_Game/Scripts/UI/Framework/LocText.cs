using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Keeps a TMP text localized (re-applies Loc.T on language change).</summary>
    [DisallowMultipleComponent]
    public class LocText : MonoBehaviour
    {
        public string key;
        public object[] args;

        TMP_Text _text;
        bool _subscribed;

        /// <summary>The TMP text on this GameObject.</summary>
        public TMP_Text Text => _text != null ? _text : (_text = GetComponent<TMP_Text>());

        public void Set(string key, params object[] args)
        {
            this.key = key;
            this.args = args;
            Refresh();
        }

        public void Refresh()
        {
            if (string.IsNullOrEmpty(key)) return;
            var t = Text;
            if (t == null) return;
            t.text = args != null && args.Length > 0 ? Loc.T(key, args) : Loc.T(key);
        }

        /// <summary>Stops localizing (the text keeps its current value). Used when raw text is assigned.</summary>
        public void Clear()
        {
            key = null;
            args = null;
        }

        void OnEnable()
        {
            if (!_subscribed)
            {
                Loc.OnLanguageChanged += Refresh;
                _subscribed = true;
            }
            Refresh(); // the language may have changed while this text was inactive
        }

        void OnDisable()
        {
            if (_subscribed)
            {
                Loc.OnLanguageChanged -= Refresh;
                _subscribed = false;
            }
        }
    }
}
