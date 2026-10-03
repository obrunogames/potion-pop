using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    /// <summary>
    /// Collection tab: album chips (one per world, accent colored with the album's first card; locked worlds show a
    /// padlock), album header (world name and art, x/9 progress, reward preview, Claim or Claimed), a 3x3 grid of the
    /// 9 magical cards (owned = gold frame + item art + name, missing = card back with the item's silhouette and "?");
    /// tapping an owned card opens the card detail popup with a wiggle.
    /// </summary>
    public class CollectionScreen : TabScreen
    {
        public override ScreenId Id => ScreenId.Collection;

        const float TotalH = 68f, ChipH = 150f, HeaderH = 330f;

        sealed class Chip
        {
            public AreaInfo area;
            public RectTransform root, visual;
            public Image bg, lip, ring, icon, lockIcon;
            public TMP_Text count;
            public Badge badge;
        }

        sealed class CardCell
        {
            public string cardId;
            public bool owned;
            public RectTransform root, visual;
        }

        float _w;
        RectTransform _totalPill;
        TMP_Text _totalText;
        RectTransform _chipsRow;
        readonly List<Chip> _chips = new List<Chip>();
        ScrollRect _scroll;
        RectTransform _list;
        RectTransform _header;
        RectTransform _grid;
        readonly List<CardCell> _cells = new List<CardCell>();
        string _selected;
        float _sparkleTimer;
        readonly Vector3[] _corners = new Vector3[4];
        readonly Vector3[] _viewCorners = new Vector3[4];

        public override void Build()
        {
            BuildFrame("tabs.collection.title", DS.Colors.Pink);
            BuildLayout();
            Collection.OnChanged += RequestRefresh;
            Progress.OnLevelChanged += OnLevelChanged;
            Loc.OnLanguageChanged += RequestRefresh;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Collection.OnChanged -= RequestRefresh;
            Progress.OnLevelChanged -= OnLevelChanged;
            Loc.OnLanguageChanged -= RequestRefresh;
        }

        void OnLevelChanged(int level) => RequestRefresh();

        protected override void OnLayoutChanged()
        {
            if (Mathf.Abs(ContentWidth - _w) < 1f) return;
            BuildLayout();
            if (IsVisible) Refresh(false);
        }

        // ---------------------------------------------------------------------------------------- layout

        void BuildLayout()
        {
            for (int i = Body.childCount - 1; i >= 0; i--)
            {
                var child = Body.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            _chips.Clear();
            _cells.Clear();
            _header = null;
            _grid = null;
            _w = ContentWidth;

            _totalPill = CommonUI.GlassPill(Body, new Vector2(Mathf.Min(_w, 560f), TotalH), "icon_collection", out _totalText);
            UIKit.Place(_totalPill, new Vector2(0.5f, 1f), new Vector2(Mathf.Min(_w, 560f), TotalH), Vector2.zero);

            _chipsRow = UIKit.Rect("Albums", Body);
            UIKit.Place(_chipsRow, new Vector2(0.5f, 1f), new Vector2(_w, ChipH + 20f), new Vector2(0f, -(TotalH + DS.Space.XS)));
            var areas = Catalog.Areas;
            int n = areas.Count;
            if (n > 0)
            {
                float gap = DS.Space.S;
                float chipW = Mathf.Min(184f, (_w - gap * (n - 1)) / n);
                float total = chipW * n + gap * (n - 1);
                for (int i = 0; i < n; i++)
                {
                    var chip = BuildChip(areas[i], chipW);
                    UIKit.Place(chip.root, new Vector2(0.5f, 0.5f), new Vector2(chipW, ChipH),
                        new Vector2(-total * 0.5f + chipW * 0.5f + i * (chipW + gap), 0f));
                    _chips.Add(chip);
                }
            }

            var scroll = UIKit.ScrollView(Body, new Vector2(_w, 600f), out _list, DS.Space.M, DS.Space.S);
            _scroll = scroll;
            UIKit.Stretch((RectTransform)scroll.transform, 0f, TotalH + DS.Space.XS + ChipH + 20f + DS.Space.XS, 0f, 0f);
            // Stays the last child (header/grid are inserted at 0/1): the last card row can scroll clear of the raised
            // bottom-nav tile.
            UIKit.Spacer(_list, new Vector2(_w, DS.Space.XL));
        }

        Chip BuildChip(AreaInfo area, float w)
        {
            var c = new Chip { area = area };
            c.root = UIKit.Rect("Chip_" + area.id, _chipsRow);
            c.visual = UIKit.Stretch(UIKit.Rect("Visual", c.root));
            Color accent = DS.AreaAccent(area.id);
            c.ring = UIKit.RoundedRect(c.visual, new Vector2(w, ChipH), Color.white, 40f);
            UIKit.Stretch(c.ring.rectTransform, -8f, -8f, -8f, -8f);
            c.lip = UIKit.RoundedRect(c.visual, new Vector2(w, ChipH), Color.Lerp(accent, DS.Colors.Ink, 0.35f), 36f);
            UIKit.Stretch(c.lip.rectTransform);
            c.bg = UIKit.RoundedRect(c.visual, new Vector2(w, ChipH), accent, 36f);
            UIKit.Stretch(c.bg.rectTransform, 0f, 0f, 0f, 10f);
            var shine = UIKit.RoundedRect(c.bg.rectTransform, new Vector2(w - 24f, 34f), DS.WithAlpha(Color.white, 0.3f), 17f);
            UIKit.Place(shine.rectTransform, new Vector2(0.5f, 1f), new Vector2(w - 24f, 34f), new Vector2(0f, -8f));

            string iconCard = area.cardIds != null && area.cardIds.Length > 0 ? area.cardIds[0] : null;
            // Icon (top) and x/9 count (bottom) must not overlap: 150 = 8 + 84 icon + 4 + 44 count + 10 lip.
            float iconS = Mathf.Min(w * 0.55f, 84f);
            c.icon = UIKit.Image(c.visual, iconCard != null ? Catalog.CardSprite(iconCard) : "icon_collection", new Vector2(iconS, iconS));
            UIKit.Place(c.icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(iconS, iconS), new Vector2(0f, -8f));
            c.lockIcon = UIKit.Image(c.visual, "icon_lock", new Vector2(iconS * 0.8f, iconS * 0.8f));
            UIKit.Place(c.lockIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(iconS * 0.8f, iconS * 0.8f), new Vector2(0f, -14f));

            c.count = CommonUI.Label(c.visual, "", TextStyle.Badge, new Vector2(w - 16f, 44f), TextAlignmentOptions.Center, 34f);
            UIKit.Place(c.count.rectTransform, new Vector2(0.5f, 0f), new Vector2(w - 16f, 44f), new Vector2(0f, 14f));

            var chip = c;
            CommonUI.MakeTappable(c.root, c.visual, () => OnChipTap(chip));
            return c;
        }

        // ---------------------------------------------------------------------------------------- refresh

        protected override void Refresh(bool animate)
        {
            if (_chips.Count == 0) return;
            if (_selected == null || !Collection.IsAreaUnlocked(_selected) || animate) _selected = DefaultAlbum();
            _totalText.text = Loc.T("tabs.col.total", Collection.TotalOwned, Collection.TotalCards);
            RefreshChips(animate);
            BuildAlbum(animate);
            if (animate)
            {
                CommonUI.PopIn(_totalPill, 0.05f);
                _scroll.StopMovement();
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>The current area's album when unlocked, else the first unlocked one.</summary>
        string DefaultAlbum()
        {
            var current = Areas.AreaForLevel(Progress.CurrentLevel);
            if (_selected != null && Collection.IsAreaUnlocked(_selected) && current != null && !Collection.IsAreaUnlocked(current.id))
                return _selected;
            if (current != null && Collection.IsAreaUnlocked(current.id)) return current.id;
            for (int i = 0; i < _chips.Count; i++)
                if (Collection.IsAreaUnlocked(_chips[i].area.id)) return _chips[i].area.id;
            return _chips[0].area.id;
        }

        void RefreshChips(bool animate)
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                var c = _chips[i];
                bool unlocked = Collection.IsAreaUnlocked(c.area.id);
                bool selected = c.area.id == _selected;
                Color accent = DS.AreaAccent(c.area.id);
                c.bg.color = unlocked ? accent : DS.Colors.Gray;
                c.lip.color = Color.Lerp(unlocked ? accent : DS.Colors.Gray, DS.Colors.Ink, 0.35f);
                c.ring.gameObject.SetActive(selected);
                c.icon.gameObject.SetActive(unlocked);
                c.lockIcon.gameObject.SetActive(!unlocked);
                int owned = Collection.OwnedCount(c.area.id), size = Collection.AlbumSize(c.area.id);
                c.count.text = Loc.T("quest.progress", owned, size);
                bool claimable = Collection.AlbumComplete(c.area.id) && !Collection.AlbumClaimed(c.area.id);
                if (claimable)
                {
                    if (c.badge == null)
                    {
                        c.badge = UIKit.Badge(c.visual, new Vector2(-4f, -4f));
                        c.badge.name = "Badge";
                    }
                    c.badge.SetText("!");
                }
                else if (c.badge != null) c.badge.Hide();

                float targetScale = selected ? 1.1f : 1f;
                var targetPos = new Vector2(0f, selected ? 10f : 0f);
                Tween.Kill(c.visual);
                if (animate)
                {
                    c.visual.localScale = Vector3.zero;
                    c.visual.anchoredPosition = targetPos;
                    Tween.Scale(c.visual, targetScale, DS.Motion.Slow, Ease.OutBack).SetDelay(0.05f + i * DS.Motion.Stagger);
                }
                else
                {
                    Tween.Scale(c.visual, targetScale, DS.Motion.Base, Ease.OutBack);
                    Tween.Move(c.visual, targetPos, DS.Motion.Base, Ease.OutBack);
                }
            }
        }

        void OnChipTap(Chip c)
        {
            if (!Collection.IsAreaUnlocked(c.area.id))
            {
                CommonUI.Deny(c.visual);
                UIKit.Toast(Loc.T("tabs.col.locked_area", Areas.FirstLevelOfArea(c.area.index)));
                return;
            }
            if (c.area.id == _selected)
            {
                Tween.Punch(c.visual, 0.12f, 0.3f);
                return;
            }
            _selected = c.area.id;
            AudioManager.Play(Sfx.Swoosh);
            RefreshChips(false);
            BuildAlbum(true);
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
        }

        // ---------------------------------------------------------------------------------------- album

        void BuildAlbum(bool animate)
        {
            // Deactivate first: Destroy is deferred and the layout group would keep the old ones for a frame.
            if (_header != null) { _header.gameObject.SetActive(false); Destroy(_header.gameObject); }
            if (_grid != null) { _grid.gameObject.SetActive(false); Destroy(_grid.gameObject); }
            _cells.Clear();
            var area = Catalog.GetArea(_selected);
            if (area == null) return;
            _header = BuildHeader(area);
            _grid = BuildGrid(area, animate);
            if (animate) CommonUI.PopIn(_header, 0.08f);
        }

        RectTransform BuildHeader(AreaInfo area)
        {
            Color accent = DS.AreaAccent(area.id);
            var root = UIKit.Rect("AlbumHeader", _list);
            root.sizeDelta = new Vector2(_w, HeaderH);
            root.SetSiblingIndex(0);
            var lip = UIKit.RoundedRect(root, new Vector2(_w, HeaderH), Color.Lerp(accent, DS.Colors.Ink, 0.4f), 46f);
            UIKit.Stretch(lip.rectTransform);
            var bg = UIKit.RoundedRect(root, new Vector2(_w, HeaderH), accent, 46f);
            UIKit.Stretch(bg.rectTransform, 0f, 0f, 0f, 12f);
            var shine = UIKit.NewImage(bg.rectTransform, "Shine", UISprites.Get("ui_gradient_v"), DS.WithAlpha(Color.white, 0.35f));
            UIKit.Stretch(shine.rectTransform, 10f, 8f, 10f, HeaderH * 0.45f);

            var sign = UIKit.Image(root, area.HomeBackground, new Vector2(164f, 246f));
            UIKit.Place(sign.rectTransform, new Vector2(1f, 1f), new Vector2(164f, 246f), new Vector2(-28f, -26f));
            var signMaskBg = UIKit.RoundedRect(root, new Vector2(184f, 266f), Color.white, 30f);
            UIKit.Place(signMaskBg.rectTransform, new Vector2(1f, 1f), new Vector2(184f, 266f), new Vector2(-18f, -16f));
            signMaskBg.transform.SetSiblingIndex(sign.transform.GetSiblingIndex());
            sign.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 4f);
            signMaskBg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 4f);
            sign.preserveAspect = false;

            float textW = _w - 260f;
            var title = CommonUI.LocLabel(root, area.NameKey, TextStyle.H2, new Vector2(textW, 80f), TextAlignmentOptions.Left, 62f);
            UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(textW, 80f), new Vector2(34f, -24f));

            int owned = Collection.OwnedCount(area.id), size = Mathf.Max(1, Collection.AlbumSize(area.id));
            var bar = UIKit.ProgressBar(root, new Vector2(textW - 60f, 60f));
            UIKit.Place((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(textW - 60f, 60f), new Vector2(84f, -116f));
            bar.SetIcon("icon_collection");
            bar.SetValue(0f, false);
            bar.SetValue(owned / (float)size, true);
            bar.SetLabel(Loc.T("quest.progress", owned, size));

            bool complete = Collection.AlbumComplete(area.id);
            bool claimed = Collection.AlbumClaimed(area.id);
            if (complete && !claimed)
            {
                var claim = UIKit.ButtonLoc(root, "ui.claim", ButtonColor.Green, new Vector2(Mathf.Min(460f, textW), 120f), () => ClaimAlbum(area.id));
                UIKit.Place((RectTransform)claim.transform, new Vector2(0f, 0f), new Vector2(Mathf.Min(460f, textW), 120f), new Vector2(34f, 30f));
                Tween.Pulse(claim.transform, 1.06f);
            }
            else if (claimed)
            {
                var row = UIKit.Rect("Claimed", root);
                UIKit.Place(row, new Vector2(0f, 0f), new Vector2(textW, 90f), new Vector2(34f, 40f));
                var check = UIKit.Image(row, "icon_check", new Vector2(80f, 80f));
                UIKit.Place(check.rectTransform, new Vector2(0f, 0.5f), new Vector2(80f, 80f), Vector2.zero);
                var t = CommonUI.LocLabel(row, "common.claimed", TextStyle.H3, new Vector2(textW - 100f, 80f), TextAlignmentOptions.Left, 48f);
                UIKit.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(textW - 100f, 80f), new Vector2(96f, 0f));
            }
            else
            {
                var caption = CommonUI.LocLabel(root, "tabs.col.complete_to_win", TextStyle.BodyLight, new Vector2(textW, 46f),
                    TextAlignmentOptions.Left, 34f);
                UIKit.Place(caption.rectTransform, new Vector2(0f, 0f), new Vector2(textW, 46f), new Vector2(34f, 100f));
                // 500 coins + one of each booster: the booster icons share what is left of the row.
                var rewards = Collection.AlbumRewards();
                float x = 34f;
                float boosterS = rewards.Length > 1 ? Mathf.Clamp((textW - 34f - 72f - 126f) / (rewards.Length - 1) - 4f, 40f, 60f) : 60f;
                for (int i = 0; i < rewards.Length; i++)
                {
                    float s = i == 0 ? 72f : boosterS;
                    if (x + s > textW + 20f) break;
                    var ic = UIKit.Image(root, rewards[i].IconSprite, new Vector2(s, s));
                    UIKit.Place(ic.rectTransform, new Vector2(0f, 0f), new Vector2(s, s), new Vector2(x, i == 0 ? 28f : 34f));
                    x += s + 4f;
                    if (i == 0)
                    {
                        var amt = CommonUI.Label(root, rewards[i].AmountText, TextStyle.H3, new Vector2(120f, 60f), TextAlignmentOptions.Left, 44f);
                        UIKit.Place(amt.rectTransform, new Vector2(0f, 0f), new Vector2(120f, 60f), new Vector2(x, 36f));
                        x += 122f;
                    }
                }
            }
            return root;
        }

        RectTransform BuildGrid(AreaInfo area, bool animate)
        {
            const int cols = 3;
            float gapX = DS.Space.M, gapY = DS.Space.L;
            float cw = Mathf.Min(270f, (_w - gapX * (cols - 1)) / cols);
            float ch = cw * CommonUI.CardAspect;
            int count = area.cardIds != null ? area.cardIds.Length : 0;
            int rows = (count + cols - 1) / cols;
            var grid = UIKit.Grid(_list, new Vector2(_w, rows * ch + Mathf.Max(0, rows - 1) * gapY + DS.Space.S),
                new Vector2(cw, ch), new Vector2(gapX, gapY), cols);
            grid.transform.SetSiblingIndex(1);
            for (int i = 0; i < count; i++)
            {
                string id = area.cardIds[i];
                var cell = new CardCell { cardId = id, owned = Collection.Has(id) };
                cell.root = UIKit.Rect("Card_" + id, grid.transform);
                cell.visual = UIKit.Stretch(UIKit.Rect("Visual", cell.root));
                var face = cell.owned ? CommonUI.CardFront(cell.visual, id, cw) : CommonUI.CardBack(cell.visual, cw, true, id);
                UIKit.Place(face, new Vector2(0.5f, 0.5f), face.sizeDelta, Vector2.zero);
                var c = cell;
                CommonUI.MakeTappable(cell.root, cell.visual, () => OnCardTap(c));
                _cells.Add(cell);
                if (animate)
                {
                    cell.visual.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? -8f : 8f);
                    CommonUI.PopIn(cell.visual, 0.12f + i * 0.035f);
                    Tween.Rotate(cell.visual, 0f, 0.45f, Ease.OutBack).SetDelay(0.12f + i * 0.035f);
                }
            }
            if (animate) Tween.Delay(0.2f, () => AudioManager.Play(Sfx.Swoosh)).SetLink(this);
            return (RectTransform)grid.transform;
        }

        void OnCardTap(CardCell c)
        {
            if (c.owned)
            {
                Tween.Punch(c.visual, 0.1f, 0.25f);
                CardZoomPopup.Open(c.cardId);
            }
            else
            {
                CommonUI.Deny(c.visual);
                UIKit.Toast(Loc.T("tabs.col.missing_card"));
            }
        }

        void ClaimAlbum(string areaId)
        {
            var rewards = Collection.ClaimAlbum(areaId);
            if (rewards == null || rewards.Length == 0) return;
            AudioManager.Play(Sfx.Fanfare);
            Haptics.Play(HapticType.Success);
            if (_header != null) FX.Celebrate(FX.LocalCenterOf(_header));
            RewardPopup.Open(rewards, "tabs.col.album_complete_title", () => { if (this != null) RequestRefresh(); });
        }

        // ---------------------------------------------------------------------------------------- idle sparkle

        void Update()
        {
            _sparkleTimer += Time.unscaledDeltaTime;
            if (_sparkleTimer < 1.3f) return;
            _sparkleTimer = Random.Range(-0.4f, 0.2f);
            if (_cells.Count == 0 || PopupManager.AnyOpen || _scroll == null) return;
            int start = Random.Range(0, _cells.Count);
            for (int k = 0; k < _cells.Count; k++)
            {
                var c = _cells[(start + k) % _cells.Count];
                if (!c.owned || c.visual == null || !FullyVisible(c.root)) continue;
                FX.Sparkles(null, FX.LocalCenterOf(c.visual) + new Vector2(Random.Range(-40f, 40f), Random.Range(0f, 80f)), 3);
                break;
            }
        }

        bool FullyVisible(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            _scroll.viewport.GetWorldCorners(_viewCorners);
            return _corners[0].y >= _viewCorners[0].y && _corners[1].y <= _viewCorners[1].y;
        }
    }
}
