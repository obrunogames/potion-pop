using System.Collections.Generic;
using System.Text;

namespace PotionPop
{
    /// <summary>
    /// Objectionable-name filter for the names other players see in the global ranking (App Store guideline 1.2).
    /// Pure C#: a compact list of offensive / slur / sexual roots in pt, en and es matched against a normalized copy of
    /// the name — lowercase, accents stripped, leetspeak mapped (0→o 1→i 3→e 4→a 5→s 7→t @→a $→s), anything that is not
    /// a letter used as a word separator.
    /// <list type="bullet">
    /// <item>Repeated letters stretch: each letter of a root matches a run of one or more of that letter ("fuuuck"),
    /// while a double letter in a root still needs two ("zorra" never matches the name "Zora").</item>
    /// <item>"root" = anywhere: distinctive roots, found inside any word and across separators ("f u c k", "mier da").
    /// "=word" = whole word only, "word*" = a word starting with it: short or ambiguous roots that would otherwise hit
    /// normal names (Scunthorpe, Analu, Assis, deputado, Yamashita...).</item>
    /// <item>Words are also split at camelCase humps ("BigAss") and runs of single letters are joined ("p.u.t.a").</item>
    /// </list>
    /// Not exhaustive by design: rows can also be reported and blocked from the ranking.
    /// </summary>
    public static class NameFilter
    {
        static readonly string[] Roots =
        {
            // en
            "fuck", "=fck", "=fuk", "shit*", "bullshit", "bitch", "cunt*", "dickhead", "=cock", "=cocks", "cocksuck",
            "pussy", "whore", "slut", "=ass", "asshole", "dumbass", "jackass", "fatass", "=arse", "nigger", "nigga",
            "faggot", "=fag", "=fags", "retard", "=rape", "=rapist", "porn", "penis", "vagina", "=boob", "=boobs", "=tit",
            "=tits", "=cum", "cumshot", "jizz", "dildo", "blowjob", "handjob", "milf", "orgasm", "=orgy", "=horny", "=anal",
            "=anus", "=nude", "=nudes", "=sex", "=kys", "hitler", "=nazi", "=nazis", "=chink", "=spic", "wetback", "tranny",
            "pedophil",
            // pt
            "caralh", "=porra", "buceta", "boceta", "xoxota", "=xota", "piroca", "cacete", "merda", "bosta", "foda",
            "foder", "fodid", "puta*", "puto*", "=putinha", "daputa", "=fdp", "=viado", "=viadao", "=bicha", "baitola",
            "boiola", "sapatao", "=cu", "=cus", "cuzao", "cuzinho", "arrombad", "=corno", "vagabunda", "punheta",
            "siririca", "boquete", "estupr", "nazista", "pedofil", "=sexo",
            // es
            "mierda", "joder", "jodid", "pendej", "cabron", "maricon", "chinga", "culero", "=culo", "=polla", "=zorra",
            "=perra", "=verga", "=cono", "gilipolla", "follar", "follad", "mamahuev", "mamaguev", "conchatumadre",
            "malparid", "gonorrea", "hijueputa", "hijodeputa", "putamadre", "=hdp", "negrata", "sudaca", "violador",
        };

        /// <summary>A word this short after a separator is a fragment ("cara lho"), not a real word ("Bo Stark").</summary>
        const int MaxFragment = 3;

        enum Kind { Anywhere, Word, WordPrefix }

        struct Root
        {
            public string text;
            public Kind kind;
        }

        static Root[] _roots;

        static Root[] Parsed
        {
            get
            {
                if (_roots != null) return _roots;
                var list = new Root[Roots.Length];
                for (int i = 0; i < Roots.Length; i++)
                {
                    string r = Roots[i];
                    if (r[0] == '=') list[i] = new Root { text = r.Substring(1), kind = Kind.Word };
                    else if (r[r.Length - 1] == '*') list[i] = new Root { text = r.Substring(0, r.Length - 1), kind = Kind.WordPrefix };
                    else list[i] = new Root { text = r, kind = Kind.Anywhere };
                }
                return _roots = list;
            }
        }

        /// <summary>True when the name contains an offensive word (null / empty → false).</summary>
        public static bool IsOffensive(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            // Plain words, then the same letters split at camelCase humps ("BigAss" → big, ass).
            return Check(Words(name, false)) || Check(Words(name, true));
        }

        /// <summary>
        /// Normalized words of a name: lowercase, accents stripped, leetspeak mapped, split on anything that is not a
        /// letter (and at lower→upper case changes when <paramref name="splitCase"/>).
        /// </summary>
        public static List<string> Words(string name, bool splitCase)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            var sb = new StringBuilder(name.Length);
            bool prevLower = false;
            foreach (char raw in name)
            {
                if (raw >= '̀' && raw <= 'ͯ') continue;   // combining accent (decomposed input): same word
                char c = Leet(raw);
                if (!char.IsLetter(c))
                {
                    Flush(sb, words);
                    prevLower = false;
                    continue;
                }
                bool upper = c == raw && char.IsUpper(c);   // leetspeak letters count as lowercase
                if (splitCase && upper && prevLower) Flush(sb, words);
                prevLower = !upper;
                sb.Append(Fold(char.ToLowerInvariant(c)));
            }
            Flush(sb, words);
            return words;
        }

        static void Flush(StringBuilder sb, List<string> words)
        {
            if (sb.Length == 0) return;
            words.Add(sb.ToString());
            sb.Length = 0;
        }

        static char Leet(char c)
        {
            switch (c)
            {
                case '0': return 'o';
                case '1': return 'i';
                case '3': return 'e';
                case '4':
                case '@': return 'a';
                case '5':
                case '$': return 's';
                case '7': return 't';
                default: return c;
            }
        }

        const string Accented = "áàâãäåāéèêëēíìîïīóòôõöøōúùûüūçñýÿ";
        const string Plain = "aaaaaaaeeeeeiiiiiooooooouuuuucnyy";

        static char Fold(char c)
        {
            int i = Accented.IndexOf(c);
            return i >= 0 ? Plain[i] : c;
        }

        static bool Check(List<string> words)
        {
            if (words.Count == 0) return false;
            // Runs of single letters ("p u t a", "f.u.c.k") count as one more word.
            var candidates = new List<string>(words);
            var run = new StringBuilder();
            foreach (string w in words)
            {
                if (w.Length == 1) { run.Append(w); continue; }
                if (run.Length > 1) candidates.Add(run.ToString());
                run.Length = 0;
            }
            if (run.Length > 1) candidates.Add(run.ToString());

            foreach (Root root in Parsed)
            {
                foreach (string w in candidates)
                {
                    int end;
                    switch (root.kind)
                    {
                        case Kind.Word:
                            if (MatchAt(w, 0, root.text, out end) && end == w.Length) return true;
                            break;
                        case Kind.WordPrefix:
                            if (MatchAt(w, 0, root.text, out end)) return true;
                            break;
                        default:
                            if (Contains(w, root.text)) return true;
                            break;
                    }
                }
                if (root.kind == Kind.Anywhere && SpansWords(words, root.text)) return true;
            }
            return false;
        }

        /// <summary>
        /// The root split by separators ("fu ck", "mier da", "cara lho"): it starts a word and ends at the end of a later
        /// word or inside a short fragment, never inside a real next word ("Bo Stark" is not "bosta").
        /// </summary>
        static bool SpansWords(List<string> words, string root)
        {
            for (int i = 0; i < words.Count - 1; i++)
            {
                string joined = words[i];
                for (int j = i + 1; j < words.Count; j++)
                {
                    joined += words[j];
                    if (!MatchAt(joined, 0, root, out int end)) continue;
                    if (end <= joined.Length - words[j].Length) break;   // ended in an earlier word: seen there already
                    if (end == joined.Length || words[j].Length <= MaxFragment) return true;
                    break;
                }
            }
            return false;
        }

        static bool Contains(string text, string root)
        {
            for (int i = 0; i + root.Length <= text.Length; i++)
            {
                if (i > 0 && text[i] == text[i - 1]) continue;   // inside a run: already tried from its first letter
                if (MatchAt(text, i, root, out _)) return true;
            }
            return false;
        }

        /// <summary>
        /// Matches <paramref name="root"/> at <paramref name="start"/>, run by run: each run of a letter in the root needs
        /// a run of at least as many of that letter in the text. <paramref name="end"/> = index after the matched text.
        /// </summary>
        static bool MatchAt(string text, int start, string root, out int end)
        {
            int t = start, r = 0;
            end = start;
            while (r < root.Length)
            {
                char c = root[r];
                int need = 1;
                while (r + need < root.Length && root[r + need] == c) need++;
                int have = 0;
                while (t + have < text.Length && text[t + have] == c) have++;
                if (have < need) return false;
                t += have;
                r += need;
            }
            end = t;
            return true;
        }
    }
}
