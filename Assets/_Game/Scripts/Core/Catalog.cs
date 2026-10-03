using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionPop
{
    /// <summary>A world: a group of <see cref="Areas.LevelsPerArea"/> levels with its theme and card album.</summary>
    public sealed class AreaInfo
    {
        public string id;
        public int index;                 // 0-based
        public string[] cardIds;          // 9 collectible cards (album of this world)
        public Color accent;
        public string NameKey => "area." + id;           // localized world name
        public string HomeBackground => "home_" + id;    // sprite names
        public string GameBackground => "gamebg_" + id;
        /// <summary>Number of cards in this world's album.</summary>
        public int CardCount => cardIds != null ? cardIds.Length : 0;
    }

    /// <summary>
    /// World + card catalog read from Resources/catalog.json (exported by Tools/export_catalog.py):
    /// {"areas":[{"id":"forest","index":0,"accent":"#2ED6A1","cards":["glow_mushroom",...]}]}.
    /// Card art: sprite "card_&lt;id&gt;"; card name: Loc key "card.&lt;id&gt;"; world name: "area.&lt;id&gt;".
    /// A missing or broken file yields an empty catalog (one warning) instead of an exception.
    /// </summary>
    public static class Catalog
    {
        const string ResourcePath = "catalog";

#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable] class JsonArea { public string id; public int index; public string accent; public string[] cards; }
        [Serializable] class JsonRoot { public JsonArea[] areas; }
#pragma warning restore 0649

        static List<AreaInfo> _areas;
        static Dictionary<string, AreaInfo> _byId;
        static Dictionary<string, string> _areaOfCard;
        static List<string> _allCards;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _areas = null;
            _byId = null;
            _areaOfCard = null;
            _allCards = null;
        }

        public static IReadOnlyList<AreaInfo> Areas
        {
            get
            {
                EnsureLoaded();
                return _areas;
            }
        }

        public static AreaInfo GetArea(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            return _byId.TryGetValue(id, out var a) ? a : null;
        }

        public static AreaInfo GetArea(int index)
        {
            EnsureLoaded();
            return index >= 0 && index < _areas.Count ? _areas[index] : null;
        }

        public static IEnumerable<string> AllCards
        {
            get
            {
                EnsureLoaded();
                return _allCards;
            }
        }

        /// <summary>Number of cards over all albums.</summary>
        public static int CardCount
        {
            get
            {
                EnsureLoaded();
                return _allCards.Count;
            }
        }

        public static string AreaOfCard(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            EnsureLoaded();
            return _areaOfCard.TryGetValue(cardId, out var a) ? a : null;
        }

        public static string CardNameKey(string cardId) => "card." + cardId;
        public static string CardSprite(string cardId) => "card_" + cardId;

        /// <summary>Reloads Resources/catalog.json (editor tools / tests).</summary>
        public static void Reload()
        {
            ResetStatics();
            EnsureLoaded();
        }

        /// <summary>Replaces the catalog with the given JSON (tests / tools). Returns false if it could not be parsed.</summary>
        public static bool LoadFromJson(string json)
        {
            Build(Parse(json, "LoadFromJson"));
            return _areas.Count > 0;
        }

        static void EnsureLoaded()
        {
            if (_areas != null) return;
            JsonRoot root = null;
            TextAsset asset = null;
            try { asset = Resources.Load<TextAsset>(ResourcePath); }
            catch (Exception) { /* treated as missing */ }
            if (asset != null) root = Parse(asset.text, "Resources/" + ResourcePath + ".json");
            else Debug.LogWarning("[Catalog] Resources/" + ResourcePath + ".json not found; using an empty catalog.");
            Build(root);
        }

        static JsonRoot Parse(string json, string source)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<JsonRoot>(json); }
            catch (Exception e)
            {
                Debug.LogWarning("[Catalog] Could not parse " + source + ": " + e.Message);
                return null;
            }
        }

        static void Build(JsonRoot root)
        {
            _areas = new List<AreaInfo>();
            _byId = new Dictionary<string, AreaInfo>(StringComparer.Ordinal);
            _areaOfCard = new Dictionary<string, string>(StringComparer.Ordinal);
            _allCards = new List<string>();
            if (root == null || root.areas == null) return;

            var parsed = new List<JsonArea>();
            foreach (var a in root.areas)
                if (a != null && !string.IsNullOrEmpty(a.id)) parsed.Add(a);
            // Stable sort by declared index (ties keep file order).
            for (int i = 1; i < parsed.Count; i++)
            {
                var item = parsed[i];
                int j = i - 1;
                while (j >= 0 && parsed[j].index > item.index) { parsed[j + 1] = parsed[j]; j--; }
                parsed[j + 1] = item;
            }

            foreach (var a in parsed)
            {
                if (_byId.ContainsKey(a.id))
                {
                    Debug.LogWarning("[Catalog] Duplicate area id: " + a.id);
                    continue;
                }
                var cards = new List<string>();
                if (a.cards != null)
                    foreach (var p in a.cards)
                    {
                        if (string.IsNullOrEmpty(p) || _areaOfCard.ContainsKey(p)) continue;
                        cards.Add(p);
                        _areaOfCard[p] = a.id;
                        _allCards.Add(p);
                    }
                var info = new AreaInfo
                {
                    id = a.id,
                    index = _areas.Count,   // re-indexed 0..n-1 so GetArea(index) and area numbers always line up
                    cardIds = cards.ToArray(),
                    accent = ParseColor(a.accent),
                };
                _areas.Add(info);
                _byId[a.id] = info;
            }
        }

        static Color ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex))
            {
                string h = hex[0] == '#' ? hex : "#" + hex;
                if (ColorUtility.TryParseHtmlString(h, out var c)) return c;
            }
            return new Color(0.482f, 0.302f, 1f);   // brand purple
        }
    }

    /// <summary>World progression: 20 levels per world, then the world themes cycle.</summary>
    public static class Areas
    {
        public const int LevelsPerArea = 20;
        /// <summary>Unbounded area number (0,1,2,... grows forever). Use AreaForLevel for the theme.</summary>
        public static int AreaNumberForLevel(int level) => Math.Max(0, level - 1) / LevelsPerArea;

        /// <summary>Theme of a level: area number modulo the number of areas (null when the catalog is empty).</summary>
        public static AreaInfo AreaForLevel(int level) => AreaForNumber(AreaNumberForLevel(level));

        /// <summary>Theme of an (unbounded) area number.</summary>
        public static AreaInfo AreaForNumber(int areaNumber)
        {
            var areas = Catalog.Areas;
            if (areas.Count == 0) return null;
            int i = areaNumber % areas.Count;
            return areas[i < 0 ? i + areas.Count : i];
        }

        /// <summary>1..20</summary>
        public static int LevelInArea(int level) => (Math.Max(1, level) - 1) % LevelsPerArea + 1;

        /// <summary>First level of an area number.</summary>
        public static int FirstLevelOfArea(int areaNumber) => Math.Max(0, areaNumber) * LevelsPerArea + 1;

        /// <summary>Last level of an area number.</summary>
        public static int LastLevelOfArea(int areaNumber) => FirstLevelOfArea(areaNumber) + LevelsPerArea - 1;

        /// <summary>Stars a whole world can give (3 per level).</summary>
        public const int MaxStarsPerArea = LevelsPerArea * 3;

        /// <summary>How many times the world themes went round before this area (0 for the first 6 worlds).</summary>
        public static int CycleOfArea(int areaNumber)
        {
            int n = Catalog.Areas.Count;
            return n > 0 ? Math.Max(0, areaNumber) / n : 0;
        }
    }
}
