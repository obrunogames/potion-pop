// ============================================================================================================
// Daily Quests: three quest cards (icon, title, progress bar x/y, reward, Claim button or a check when claimed)
// and the countdown to the next set (midnight). Claiming flies the reward to the top bar.
// ============================================================================================================
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class QuestsPopup : Popup
    {
        const float RowH = 250f, RowGap = 24f;

        readonly List<Row> _rows = new List<Row>();
        TMP_Text _countdown, _done;
        float _tick;
        bool _dirty;
        int _day;

        protected override string TitleKey => "home.quests_title";
        protected override Vector2 PanelSize => new Vector2(DS.Space.PopupWidth, 1150f);

        public static void Open()
        {
            if (PopupManager.IsOpen<QuestsPopup>()) return;
            PopupManager.Show<QuestsPopup>();
        }

        protected override void BuildContent(RectTransform content)
        {
            float w = content.rect.width;
            var quests = Quests.Today();
            _day = TimeUtil.Today;
            var items = new List<RectTransform>();
            for (int i = 0; i < quests.Count; i++)
            {
                var row = Row.Create(this, content, quests[i], w);
                UIKit.Place(row.Root, new Vector2(0.5f, 1f), new Vector2(w, RowH), Vector2.zero);
                row.Root.pivot = new Vector2(0.5f, 0.5f);
                row.Root.anchoredPosition = new Vector2(0f, -(i * (RowH + RowGap) + RowH * 0.5f));
                _rows.Add(row);
                items.Add(row.Root);
            }
            MetaUI.PopInAll(items, 0.12f, 0.08f);

            float y = quests.Count * (RowH + RowGap) + 16f;
            _done = MetaUI.LocLabel(content, "home.quests_done", TextStyle.Body, 40f, new Vector2(w, 60f), new Vector2(0.5f, 1f), new Vector2(0f, -y));
            _done.color = DS.Colors.Primary;
            _countdown = MetaUI.Label(content, "", TextStyle.H3, 44f, new Vector2(w, 64f), new Vector2(0.5f, 0f), new Vector2(0f, 10f));
            _countdown.color = DS.Colors.Orange;

            Quests.OnChanged += OnQuestsChanged;
            RefreshFooter();
        }

        void OnDestroy() => Quests.OnChanged -= OnQuestsChanged;

        void OnQuestsChanged() => _dirty = true;

        void Update()
        {
            if (IsClosing) return;
            if (_dirty)
            {
                _dirty = false;
                var quests = Quests.Today();
                // A new day replaced the set (icons and rewards are built once): rebuild by reopening.
                if (NewSet(quests))
                {
                    Reopen();
                    return;
                }
                for (int i = 0; i < _rows.Count; i++) _rows[i].Refresh(quests[i], false);
                RefreshFooter();
            }
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            // Midnight passed while open: Quests.Today() rolls the set over (new quests, progress reset) without an
            // event — re-read it through the dirty path next frame.
            if (TimeUtil.Today != _day)
            {
                _day = TimeUtil.Today;
                _dirty = true;
            }
            RefreshFooter();
        }

        bool NewSet(IReadOnlyList<QuestView> quests)
        {
            if (quests.Count != _rows.Count) return true;
            for (int i = 0; i < quests.Count; i++)
                if (quests[i].kind != _rows[i].Kind || quests[i].reward.type != _rows[i].RewardType ||
                    quests[i].reward.amount != _rows[i].RewardAmount) return true;
            return false;
        }

        void Reopen()
        {
            CloseImmediate();
            Open();
        }

        void RefreshFooter()
        {
            if (_countdown != null) _countdown.text = Loc.T("quest.new_in", TimeUtil.FormatDuration(Quests.SecondsToReset));
            if (_done != null)
            {
                bool all = _rows.Count > 0;
                for (int i = 0; i < _rows.Count; i++) all &= _rows[i].Claimed;
                _done.gameObject.SetActive(all);
            }
        }

        void Claim(Row row)
        {
            if (row == null || IsClosing) return;
            var reward = Quests.Claim(row.Index);
            if (reward.IsEmpty) return;
            var bar = TopBar.Instance;
            var from = MetaUI.WorldCenter(row.RewardIcon);
            AudioManager.Play(Sfx.Reward);
            Haptics.Play(HapticType.Success);
            MetaUI.Celebrate(row.RewardIcon, 10, 14);
            if (bar != null && bar.TargetFor(reward.type) != null)
            {
                bar.Hold(reward);
                TopBar.Fly(new[] { reward }, from);
            }
            else
            {
                var sm = ScreenManager.Instance;
                if (sm != null)
                    FX.FloatingText(sm.FxLayer, FX.LocalCenterOf(row.RewardIcon) + new Vector2(0f, 70f), reward.AmountText, TextStyle.H2, DS.Colors.Accent);
            }
            var quests = Quests.Today();
            if (row.Index >= 0 && row.Index < quests.Count) row.Refresh(quests[row.Index], true);
            _dirty = false;
            RefreshFooter();
        }

        static string IconFor(QuestKind kind)
        {
            switch (kind)
            {
                case QuestKind.WinLevels: return "icon_trophy";
                case QuestKind.CompleteBottles: return "icon_bottle";
                case QuestKind.ReachCombo: return "icon_flame";
                case QuestKind.UseBoosters: return "booster_undo";
                case QuestKind.CollectStars: return "icon_star";
                case QuestKind.WinHard: return "icon_skull";
                case QuestKind.WinStreak2: return "icon_medal_gold";
                case QuestKind.WinThreeStars: return "icon_star";
                default: return "icon_quest";
            }
        }

        // ---------------------------------------------------------------------------------------- row

        sealed class Row
        {
            public RectTransform Root, RewardIcon;
            public int Index;
            public bool Claimed;
            /// <summary>What this row was built for (icon and reward are built once).</summary>
            public QuestKind Kind;
            public RewardType RewardType;
            public int RewardAmount;

            Image _card;
            TMP_Text _title;
            ProgressBar _bar;
            RectTransform _reward;
            UIButton _claim;
            RectTransform _check;
            TMP_Text _claimedText;

            public static Row Create(QuestsPopup owner, RectTransform parent, QuestView q, float w)
            {
                var r = new Row { Index = q.index, Kind = q.kind, RewardType = q.reward.type, RewardAmount = q.reward.amount };
                var root = UIKit.Rect("Quest" + q.index, parent);
                root.sizeDelta = new Vector2(w, RowH);
                r.Root = root;
                r._card = UIKit.Panel(root, "panel_card", new Vector2(w, RowH));
                UIKit.Stretch(r._card.rectTransform);

                // Icon bubble.
                var bubble = UIKit.NewImage(root, "Bubble", UISprites.Circle, DS.WithAlpha(DS.Colors.BrandLight, 0.35f));
                UIKit.Place(bubble.rectTransform, new Vector2(0f, 0.5f), new Vector2(150f, 150f), new Vector2(28f, 4f));
                var icon = UIKit.Image(bubble.rectTransform, IconFor(q.kind), new Vector2(112f, 112f));
                UIKit.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(112f, 112f), Vector2.zero);

                // Title + progress.
                float midW = w - 200f - 250f;
                r._title = UIKit.Text(root, "", TextStyle.Body, new Vector2(midW, 100f), TextAlignmentOptions.Left);
                DS.Apply(r._title, TextStyle.Body, 38f);
                r._title.alignment = TextAlignmentOptions.Left;
                UIKit.Place(r._title.rectTransform, new Vector2(0f, 1f), new Vector2(midW, 100f), new Vector2(196f, -30f));
                r._bar = UIKit.ProgressBar(root, new Vector2(midW, 50f));
                UIKit.Place((RectTransform)r._bar.transform, new Vector2(0f, 0f), new Vector2(midW, 50f), new Vector2(196f, 48f));
                r._bar.label.gameObject.SetActive(true);
                DS.Apply(r._bar.label, TextStyle.Badge, 30f);

                // Right column: reward on top, claim / check below.
                var right = UIKit.Rect("Right", root);
                UIKit.Place(right, new Vector2(1f, 0.5f), new Vector2(230f, RowH - 30f), new Vector2(-18f, 0f));
                r._reward = MetaUI.RewardItem(right, q.reward, 76f, 34f);
                UIKit.Place(r._reward, new Vector2(0.5f, 1f), r._reward.sizeDelta, new Vector2(0f, -4f));
                r.RewardIcon = MetaUI.IconOf(r._reward);

                var size = new Vector2(214f, 92f);
                r._claim = UIKit.ButtonLoc(right, "ui.claim", ButtonColor.Green, size, () => owner.Claim(r));
                UIKit.Place((RectTransform)r._claim.transform, new Vector2(0.5f, 0f), size, new Vector2(0f, 2f));

                r._check = MetaUI.CheckStamp(right, 86f, false);
                UIKit.Place(r._check, new Vector2(0.5f, 0f), new Vector2(80f, 80f), new Vector2(-72f, 8f));
                // Label to the right of the stamp (it used to start under the check mark).
                r._claimedText = MetaUI.LocLabel(right, "common.claimed", TextStyle.Body, 32f, new Vector2(140f, 50f), new Vector2(0.5f, 0f), new Vector2(46f, 24f));
                r._claimedText.color = DS.Colors.Primary;
                r._claimedText.alignment = TMPro.TextAlignmentOptions.Left;
                r._claimedText.enableAutoSizing = true;
                r._claimedText.fontSizeMin = 22f;
                r._claimedText.fontSizeMax = 32f;
                UIKit.Place(r._claimedText.rectTransform, new Vector2(0.5f, 0f), new Vector2(138f, 50f), new Vector2(42f, 22f));

                r.Refresh(q, false);
                return r;
            }

            public void Refresh(QuestView q, bool animate)
            {
                Index = q.index;
                Claimed = q.claimed;
                if (_title != null) _title.text = q.Title;
                if (_bar != null)
                {
                    _bar.SetValue(q.Progress01, animate);
                    _bar.SetLabel(Loc.T("quest.progress", q.progress, q.target));
                    _bar.label.gameObject.SetActive(true);
                    _bar.SetFillColor(q.IsComplete ? Color.white : DS.Colors.Secondary);
                }
                bool claimable = q.CanClaim;
                if (_claim != null)
                {
                    var t = _claim.transform;
                    Tween.Kill(t);
                    t.localScale = Vector3.one;
                    // Only completed quests show the button; unfinished ones show their reward big instead.
                    _claim.gameObject.SetActive(claimable);
                    _claim.Interactable = claimable;
                    if (claimable) Tween.Pulse(t, 1.07f, 0.8f);
                }
                if (_check != null)
                {
                    bool was = _check.gameObject.activeSelf;
                    _check.gameObject.SetActive(q.claimed);
                    if (q.claimed && !was && animate)
                    {
                        _check.localScale = Vector3.one * 2.4f;
                        Tween.Scale(_check, 1f, 0.32f, Ease.OutBack).SetOvershoot(1.6f);
                    }
                }
                if (_claimedText != null) _claimedText.gameObject.SetActive(q.claimed);
                if (_card != null) _card.color = q.claimed ? new Color(0.88f, 0.86f, 0.92f, 1f) : Color.white;
                if (_reward != null)
                {
                    bool waiting = !claimable && !q.claimed;
                    _reward.anchoredPosition = waiting ? new Vector2(0f, -(RowH - 30f) * 0.5f + _reward.sizeDelta.y * 0.62f) : new Vector2(0f, -4f);
                    _reward.localScale = Vector3.one * (waiting ? 1.3f : 1f);
                    var g = _reward.GetComponent<CanvasGroup>();
                    if (g == null) g = _reward.gameObject.AddComponent<CanvasGroup>();
                    g.alpha = q.claimed ? 0.45f : 1f;
                    var icon = MetaUI.IconOf(_reward);
                    if (icon != null)
                    {
                        Tween.Kill(icon);
                        icon.localScale = Vector3.one;
                        if (claimable) Tween.Scale(icon, 1.12f, 0.5f, Ease.InOutSine).SetLoops(-1, true);
                    }
                }
            }
        }
    }
}
