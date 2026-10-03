// ============================================================================================================
// Developer showcase of the whole design system (open with DesignSystemPopup.Open(), e.g. from a debug menu).
// Token/style/color names are shown as raw identifiers on purpose (they are code names, not player-facing copy);
// every sentence and button caption is localized (ui.ds.* keys in Resources/Loc/ui.csv).
// ============================================================================================================
using System;
using UnityEngine;
using UnityEngine.UI;

namespace PotionPop.UI
{
    public class DesignSystemPopup : Popup
    {
        protected override string TitleKey => "ui.ds.title";
        protected override Vector2 PanelSize => new Vector2(DS.Space.PopupWidth, 1640);

        RectTransform _list;
        float _w;
        CounterPill _coins;
        long _coinValue = 1250;
        ProgressBar _bar;

        public static DesignSystemPopup Open() => PopupManager.Show<DesignSystemPopup>();

        protected override void BuildContent(RectTransform content)
        {
            Vector2 size = content.rect.size;
            var scroll = UIKit.ScrollView(content, size, out _list, DS.Space.M, DS.Space.S);
            UIKit.Stretch((RectTransform)scroll.transform);
            _w = Mathf.Max(200f, size.x - DS.Space.S * 2f);

            BuildColors();
            BuildTypography();
            BuildButtons();
            BuildIconButtons();
            BuildCounters();
            BuildControls();
            BuildRibbon();
            BuildEffects();
            UIKit.Spacer(_list, new Vector2(_w, DS.Space.L));
        }

        // ---------------------------------------------------------------------------------------- sections

        void BuildColors()
        {
            Header("ui.ds.colors");
            var tokens = new (string name, Color color)[]
            {
                ("Brand", DS.Colors.Brand), ("BrandDark", DS.Colors.BrandDark), ("BrandLight", DS.Colors.BrandLight),
                ("Ink", DS.Colors.Ink), ("InkSoft", DS.Colors.InkSoft), ("Cream", DS.Colors.Cream),
                ("CreamDark", DS.Colors.CreamDark), ("White", DS.Colors.White), ("Primary", DS.Colors.Primary),
                ("Secondary", DS.Colors.Secondary), ("Accent", DS.Colors.Accent), ("Orange", DS.Colors.Orange),
                ("Pink", DS.Colors.Pink), ("Danger", DS.Colors.Danger), ("Mint", DS.Colors.Mint),
                ("Sky", DS.Colors.Sky), ("Lavender", DS.Colors.Lavender), ("Overlay", DS.Colors.Overlay),
                ("Frost", DS.Colors.Frost), ("Gray", DS.Colors.Gray), ("grocery", DS.Colors.AreaGrocery),
                ("sweets", DS.Colors.AreaSweets), ("toys", DS.Colors.AreaToys), ("beauty", DS.Colors.AreaBeauty),
                ("fresh", DS.Colors.AreaFresh),
            };
            const int cols = 4;
            const float cellH = 150f, gap = 12f;
            float cellW = (_w - gap * (cols - 1)) / cols;
            int rows = (tokens.Length + cols - 1) / cols;
            var grid = UIKit.Grid(_list, new Vector2(_w, rows * cellH + (rows - 1) * gap), new Vector2(cellW, cellH), new Vector2(gap, gap), cols);
            foreach (var (name, color) in tokens)
            {
                var cell = UIKit.Rect(name, grid.transform);
                var rim = UIKit.RoundedRect(cell, new Vector2(cellW, 96f), DS.Colors.CreamDark, 22f);
                UIKit.Place(rim.rectTransform, new Vector2(0.5f, 1f), new Vector2(cellW, 96f), Vector2.zero);
                var swatch = UIKit.RoundedRect(rim.rectTransform, Vector2.zero, color, 18f);
                UIKit.Stretch(swatch.rectTransform, 4, 4, 4, 4);
                var label = UIKit.Text(cell, name, TextStyle.Small, new Vector2(cellW, 44f));
                UIKit.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(cellW, 44f), Vector2.zero);
            }
        }

        void BuildTypography()
        {
            Header("ui.ds.typography");
            var styles = (TextStyle[])Enum.GetValues(typeof(TextStyle));
            foreach (var style in styles)
            {
                float size = DS.Size(style);
                var t = UIKit.Text(_list, style + "  " + size, style, new Vector2(_w, size * 1.3f), TMPro.TextAlignmentOptions.Left);
                t.name = style.ToString();
            }
        }

        void BuildButtons()
        {
            Header("ui.ds.buttons");
            var colors = (ButtonColor[])Enum.GetValues(typeof(ButtonColor));
            var sizes = (ButtonSize[])Enum.GetValues(typeof(ButtonSize));
            foreach (var bs in sizes)
            {
                Vector2 dim = DS.ButtonDimensions(bs);
                Caption(bs + "  " + dim.x + "x" + dim.y);
                int cols = bs == ButtonSize.Small ? 3 : (bs == ButtonSize.Medium ? 2 : 1);
                var grid = ButtonGrid(cols, dim, colors.Length, out float scale);
                foreach (var c in colors)
                {
                    var b = UIKit.Button(Cell(grid, dim, scale), c.ToString(), c, bs, null);
                    Center(b, dim, scale);
                }
            }

            CaptionLoc("ui.ds.variants");
            Vector2 md = DS.ButtonDimensions(ButtonSize.Medium);
            var vg = ButtonGrid(2, md, 6, out float s2);
            Center(UIKit.ButtonLoc(Cell(vg, md, s2), "ui.play", ButtonColor.Green, ButtonSize.Medium, null, "icon_play"), md, s2);
            var buy = UIKit.ButtonLoc(Cell(vg, md, s2), "ui.buy", ButtonColor.Blue, ButtonSize.Medium, null);
            buy.SetPrice(300);
            Center(buy, md, s2);
            var ad = UIKit.ButtonLoc(Cell(vg, md, s2), "ui.watch_ad", ButtonColor.Purple, ButtonSize.Medium, null);
            ad.SetAdBadge(true);
            Center(ad, md, s2);
            var off = UIKit.ButtonLoc(Cell(vg, md, s2), "ui.ok", ButtonColor.Green, ButtonSize.Medium, null);
            off.Interactable = false;
            Center(off, md, s2);
            var price = UIKit.Button(Cell(vg, md, s2), "", ButtonColor.Yellow, ButtonSize.Medium, null, "booster_undo");
            price.SetPrice(300);
            Center(price, md, s2);
            Center(UIKit.ButtonLoc(Cell(vg, md, s2), "ui.free", ButtonColor.Orange, ButtonSize.Medium, null, "icon_gift"), md, s2);
        }

        void BuildIconButtons()
        {
            Header("ui.ds.icon_buttons");
            var row = UIKit.HRow(_list, new Vector2(_w, 150f), DS.Space.S);
            var settings = UIKit.IconButton(row.transform, "icon_settings", 104f, null);
            UIKit.Badge(settings.transform, new Vector2(-8f, -8f)).SetCount(3);
            UIKit.IconButton(row.transform, "icon_pause", 104f, null);
            var shop = UIKit.IconButton(row.transform, "icon_shop", 104f, null);
            UIKit.Badge(shop.transform, new Vector2(-8f, -8f)).SetText("!");
            UIKit.IconButton(row.transform, "booster_undo", 140f, null, "btn_square");
            UIKit.IconButton(row.transform, "booster_wand", 140f, null, "btn_square");
            UIKit.IconButton(row.transform, "btn_round_close", 104f, null);
        }

        void BuildCounters()
        {
            Header("ui.ds.counters");
            var row = UIKit.HRow(_list, new Vector2(_w, 120f), DS.Space.L);
            _coins = UIKit.CounterPill(row.transform, "icon_coin", 340f, AddCoins);
            _coins.SetValue(_coinValue, false);
            var stars = UIKit.CounterPill(row.transform, "icon_star", 300f, null);
            stars.SetValue(4200, false);

            var row2 = UIKit.HRow(_list, new Vector2(_w, 120f), DS.Space.L);
            var hearts = UIKit.CounterPill(row2.transform, "icon_heart", 300f, null);
            hearts.SetText(Loc.T("ui.full"));
            UIKit.ButtonLoc(row2.transform, "ui.ds.fly", ButtonColor.Yellow, ButtonSize.Small, () => FlyCoins(5), "icon_coin");

            var row3 = UIKit.HRow(_list, new Vector2(_w, 130f), DS.Space.L);
            // 340 = Small button (260) + row spacing (40) + room for the star icon overhanging the bar's left end.
            _bar = UIKit.ProgressBar(row3.transform, new Vector2(_w - 340f, 64f));
            _bar.SetIcon("icon_star");
            SetBar(0.65f, false);
            UIKit.ButtonLoc(row3.transform, "ui.ds.randomize", ButtonColor.Blue, ButtonSize.Small,
                () => SetBar(UnityEngine.Random.Range(0.02f, 1f), true));
        }

        void BuildControls()
        {
            Header("ui.ds.controls");
            var row = UIKit.HRow(_list, new Vector2(_w, 120f), DS.Space.L);
            UIKit.Toggle(row.transform, true, null);
            UIKit.Toggle(row.transform, false, null);
            foreach (var text in new[] { "1", "!", "99+" })
            {
                var holder = UIKit.Rect("BadgeHolder", row.transform);
                holder.sizeDelta = new Vector2(90f, 90f);
                var bg = UIKit.RoundedRect(holder, Vector2.zero, DS.Colors.CreamDark, 24f);
                UIKit.Stretch(bg.rectTransform);
                UIKit.Badge(holder, new Vector2(-6f, -6f)).SetText(text);
            }
        }

        void BuildRibbon()
        {
            Header("ui.ds.ribbon");
            float w = Mathf.Min(720f, _w);
            float h = UIKit.RibbonHeight(w);
            var holder = UIKit.Rect("RibbonHolder", _list);
            holder.sizeDelta = new Vector2(_w, h + DS.Space.S);
            UIKit.Place(UIKit.Ribbon(holder, "ui.ds.ribbon_sample", w), new Vector2(0.5f, 0.5f), new Vector2(w, h), Vector2.zero);
        }

        void BuildEffects()
        {
            Header("ui.ds.effects");
            Vector2 dim = DS.ButtonDimensions(ButtonSize.Small);
            var grid = ButtonGrid(3, dim, 9, out float s);
            Fx(grid, dim, s, "ui.ds.burst", ButtonColor.Pink, b =>
                FX.Burst(null, FX.LocalCenterOf(b), "ui_star_small", 26, DS.Colors.Gold));
            Fx(grid, dim, s, "ui.ds.sparkles", ButtonColor.Yellow, b => FX.Sparkles(null, FX.LocalCenterOf(b), 12));
            Fx(grid, dim, s, "ui.ds.confetti", ButtonColor.Purple, b => FX.Confetti(null, 120));
            Fx(grid, dim, s, "ui.ds.ring", ButtonColor.Blue, b => FX.Ring(null, FX.LocalCenterOf(b), DS.Colors.Accent, 420f));
            Fx(grid, dim, s, "ui.ds.float", ButtonColor.Green, b =>
                FX.FloatingText(null, FX.LocalCenterOf(b) + new Vector2(0f, 60f), Loc.T("ui.ds.float_sample"), TextStyle.Display, DS.Colors.Accent));
            Fx(grid, dim, s, "ui.ds.shake", ButtonColor.Red, b => FX.ScreenShake());
            Fx(grid, dim, s, "ui.ds.toast", ButtonColor.Orange, b => UIKit.Toast(Loc.T("ui.ds.toast_sample")));
            Fx(grid, dim, s, "ui.ds.fly", ButtonColor.Yellow, b => FlyCoins(8));
            Fx(grid, dim, s, "ui.ds.celebrate", ButtonColor.Pink, b => FX.Celebrate(FX.LocalCenterOf(b)));
        }

        // ---------------------------------------------------------------------------------------- actions

        void AddCoins()
        {
            _coinValue += 250;
            if (_coins != null) _coins.SetValue(_coinValue);
        }

        void FlyCoins(int count)
        {
            if (_coins == null) return;
            long target = _coinValue + count * 10;
            _coinValue = target;
            var from = Panel != null ? Panel.TransformPoint(Panel.rect.center) : _coins.transform.position;
            UIKit.FlyRewards("icon_coin", count, from, _coins.IconTarget, null, () =>
            {
                if (_coins != null) _coins.SetValue(target);
            });
        }

        void SetBar(float v, bool animate)
        {
            if (_bar == null) return;
            _bar.SetValue(v, animate);
            _bar.SetLabel(Mathf.RoundToInt(v * 100f) + "%");
        }

        // ---------------------------------------------------------------------------------------- helpers

        void Header(string key)
        {
            var block = UIKit.Rect("Header", _list);
            block.sizeDelta = new Vector2(_w, 92f);
            var t = UIKit.LocText(block, key, TextStyle.H3, new Vector2(_w, 70f));
            t.alignment = TMPro.TextAlignmentOptions.Left;
            UIKit.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(_w, 70f), Vector2.zero);
            var line = UIKit.RoundedRect(block, new Vector2(_w, 6f), DS.Colors.CreamDark, 3f);
            UIKit.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(_w, 6f), new Vector2(0f, 6f));
        }

        void Caption(string text)
        {
            var t = UIKit.Text(_list, text, TextStyle.Small, new Vector2(_w, 48f), TMPro.TextAlignmentOptions.Left);
            t.name = "Caption";
        }

        void CaptionLoc(string key)
        {
            var t = UIKit.LocText(_list, key, TextStyle.Small, new Vector2(_w, 48f));
            t.alignment = TMPro.TextAlignmentOptions.Left;
        }

        GridLayoutGroup ButtonGrid(int cols, Vector2 dim, int count, out float scale)
        {
            const float gap = 14f;
            float cellW = (_w - gap * (cols - 1)) / cols;
            scale = Mathf.Min(1f, cellW / dim.x);
            float cellH = dim.y * scale;
            int rows = (count + cols - 1) / cols;
            return UIKit.Grid(_list, new Vector2(_w, rows * cellH + (rows - 1) * gap), new Vector2(cellW, cellH), new Vector2(gap, gap), cols);
        }

        static Transform Cell(GridLayoutGroup grid, Vector2 dim, float scale) => UIKit.Rect("Cell", grid.transform);

        static void Center(UIButton b, Vector2 dim, float scale)
        {
            if (b == null) return;
            var rt = (RectTransform)b.transform;
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), dim, Vector2.zero);
            rt.localScale = Vector3.one * scale;
        }

        void Fx(GridLayoutGroup grid, Vector2 dim, float scale, string key, ButtonColor color, Action<RectTransform> action)
        {
            UIButton b = null;
            b = UIKit.ButtonLoc(Cell(grid, dim, scale), key, color, ButtonSize.Small, () => action((RectTransform)b.transform));
            Center(b, dim, scale);
        }
    }
}
