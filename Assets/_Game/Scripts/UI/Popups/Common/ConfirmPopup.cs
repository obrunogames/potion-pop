using System;
using TMPro;
using UnityEngine;

namespace PotionPop.UI
{
    /// <summary>Generic yes/no dialog. onResult(true) = yes. Closing (X, overlay, back) answers no. onResult runs once,
    /// after the popup left the stack. Destructive actions get a red "yes" button.</summary>
    public class ConfirmPopup : Popup
    {
        string _titleKey, _messageKey, _yesKey, _noKey;
        object[] _args;
        bool _destructive;
        Action<bool> _onResult;
        bool _answered;
        UIButton _yes, _no;

        protected override string TitleKey => _titleKey;
        protected override Vector2 PanelSize => new Vector2(880f, string.IsNullOrEmpty(_titleKey) ? 640f : 760f);

        public static void Open(string titleKey, string messageKey, string yesKey, string noKey, Action<bool> onResult) =>
            Open(titleKey, messageKey, yesKey, noKey, onResult, false);

        /// <summary>Same as Open, with a Danger-colored "yes" when destructive, and optional message format args.</summary>
        public static void Open(string titleKey, string messageKey, string yesKey, string noKey, Action<bool> onResult,
            bool destructive, params object[] messageArgs)
        {
            PopupManager.Show<ConfirmPopup>(p =>
            {
                p._titleKey = titleKey;
                p._messageKey = messageKey;
                p._yesKey = string.IsNullOrEmpty(yesKey) ? "ui.yes" : yesKey;
                p._noKey = noKey;
                p._onResult = onResult;
                p._destructive = destructive;
                p._args = messageArgs;
            });
        }

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            const float buttonH = 140f;

            var msg = UIKit.LocText(content, _messageKey, TextStyle.Body, new Vector2(size.x, size.y - buttonH - 40f), _args ?? Array.Empty<object>());
            if (msg != null)
            {
                DS.Apply(msg, TextStyle.Body, 46f);
                msg.alignment = TextAlignmentOptions.Center;
                UIKit.Stretch(msg.rectTransform, 8f, 0f, 8f, buttonH + 40f);
            }

            bool two = !string.IsNullOrEmpty(_noKey);
            float bw = two ? (size.x - DS.Space.M) * 0.5f : Mathf.Min(size.x, 480f);
            var yesColor = _destructive ? ButtonColor.Red : ButtonColor.Green;
            _yes = UIKit.ButtonLoc(content, _yesKey, yesColor, new Vector2(bw, buttonH), () => Answer(true));
            var yrt = (RectTransform)_yes.transform;
            if (two)
            {
                _no = UIKit.ButtonLoc(content, _noKey, _destructive ? ButtonColor.Blue : ButtonColor.Gray, new Vector2(bw, buttonH), () => Answer(false));
                var nrt = (RectTransform)_no.transform;
                UIKit.Place(nrt, new Vector2(0f, 0f), new Vector2(bw, buttonH), Vector2.zero);
                UIKit.Place(yrt, new Vector2(1f, 0f), new Vector2(bw, buttonH), Vector2.zero);
                CommonUI.PopIn(nrt, 0.12f);
            }
            else UIKit.Place(yrt, new Vector2(0.5f, 0f), new Vector2(bw, buttonH), Vector2.zero);
            var yesButton = _yes;
            CommonUI.PopIn(yrt, two ? 0.18f : 0.12f).OnComplete(() =>
            {
                if (!_destructive && yesButton != null) Tween.Pulse(yesButton.transform, 1.04f);
            });
        }

        void Answer(bool yes)
        {
            if (_answered) return;
            _answered = true;
            Close();
            CommonUI.SafeInvoke(_onResult, yes);
        }

        protected override void OnClosing()
        {
            if (_answered) return;
            _answered = true;
            // Close() already removed this popup from the stack: callers see the real top.
            CommonUI.SafeInvoke(_onResult, false);
        }
    }
}
