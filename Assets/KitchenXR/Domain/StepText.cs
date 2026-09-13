using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace KitchenXR.Domain
{
    /// <summary>
    /// <see cref="StepText.ExpandGroups"/> の結果。本文は出典のまま、添え行だけを別に持つ。
    /// </summary>
    public readonly struct StepTextResult
    {
        /// <summary>説明の本文。出典の文をそのまま返す（書き換えない）。</summary>
        public string Text { get; }

        /// <summary>説明の下に添える行。参照の出てきた順。無ければ空。</summary>
        public IReadOnlyList<string> Notes { get; }

        public StepTextResult(string text, IReadOnlyList<string> notes)
        {
            Text = text ?? string.Empty;
            Notes = notes ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// 工程の説明の「グループ参照」を材料名に開く純粋関数（UnityEngine 非依存）。
    ///
    /// レシピの文は「合びき肉と(B)を加えて炒めます」のように材料をまとめて指すことがある。
    /// 材料の板では (B) の行が強調されるが、工程の板の説明だけを見ていると
    /// 「(B) って何だったか」が分からない。そこで**本文は触らずに**、下へ
    /// `(B)＝しょうゆ 大さじ1・みりん 大さじ1・砂糖 小さじ1` の1行を添える。
    ///
    /// 本文を書き換えないのは出典の文を保つため（サーバが 100 文字以内を保証している
    /// のは本文の長さであって、開いた後の長さではない）。
    ///
    /// 当たる表記は <see cref="ExpandGroups"/> の実装に列挙してある。**参照が在っても
    /// 材料側に同じグループが無ければ何も添えない**——空振りの行を出さない。
    /// </summary>
    public static class StepText
    {
        /// <summary>
        /// グループ名に付く語。長いものから先に並べる（「合わせ調味料A」を「調味料」で切らない）。
        /// 「調味料A」のように後ろに英字が付く形と、語そのものがグループ名の形の両方に使う。
        /// </summary>
        private static readonly string[] GroupWords =
        {
            "合わせ調味料", "合わせ調味液", "混ぜ調味料", "合わせだれ", "合わせタレ",
            "調味料", "漬け汁", "つけ汁", "煮汁", "下味", "たれ", "タレ", "ソース",
        };

        /// <summary>語だけでグループを指せるもの（「調味料」「たれ」単体は普通の名詞なので採らない）。</summary>
        private static readonly string[] StandaloneGroupWords =
        {
            "合わせ調味料", "合わせ調味液", "混ぜ調味料", "合わせだれ", "合わせタレ",
        };

        private const string Letter = "[A-Za-zＡ-Ｚａ-ｚ]";

        // (A)・（A）・[A]・［A］・【A】
        private static readonly Regex Bracketed =
            new Regex(@"[(（\[［【〔]\s*(" + Letter + @")\s*[)）\]］】〕]", RegexOptions.Compiled);

        // 調味料A・合わせ調味料Ｂ（語＋英字。括弧はあってもなくてもよい）
        private static readonly Regex WordLetter =
            new Regex("(?:" + string.Join("|", GroupWords) + @")\s*[(（\[［]?\s*(" + Letter + @")\s*[)）\]］]?",
                RegexOptions.Compiled);

        // A の材料・Ａの調味料
        private static readonly Regex LetterOf =
            new Regex("(" + Letter + @")\s*の\s*(?:材料|調味料|合わせ調味料)", RegexOptions.Compiled);

        // 裸の全角英字（Ａ）。半角の裸の A は普通の文の中に出過ぎるので採らない。
        private static readonly Regex BareWideLetter =
            new Regex(@"(?<![A-Za-zＡ-Ｚａ-ｚ])([Ａ-Ｚ])(?![A-Za-zＡ-Ｚａ-ｚ])", RegexOptions.Compiled);

        // 語だけの参照（「合わせ調味料を加える」）。
        private static readonly Regex StandaloneWord =
            new Regex("(" + string.Join("|", StandaloneGroupWords) + ")", RegexOptions.Compiled);

        /// <summary>
        /// 説明の中のグループ参照を材料名に開く。本文は返り値の <see cref="StepTextResult.Text"/> に
        /// そのまま入り、開いた行は <see cref="StepTextResult.Notes"/> に1グループ1行で入る。
        ///
        /// 当たる表記:
        ///   1. 括弧つきの英字 —— `(A)`・`（A）`・`[A]`・`［A］`・`【A】`（半角/全角どちらも）
        ///   2. 語＋英字 —— `調味料A`・`合わせ調味料Ｂ`（括弧はあってもなくてもよい）
        ///   3. 英字＋「の材料」 —— `A の材料`・`Ａの調味料`
        ///   4. 裸の全角英字 —— `Ａ`（半角の裸の `A` は採らない。普通の文に出過ぎる）
        ///   5. 語そのもの —— `合わせ調味料`・`合わせだれ`（「調味料」単体は採らない）
        ///
        /// 照合は <see cref="NormalizeGroupKey"/> で正規化した鍵どうしで行うので、材料側の
        /// `group` が `A`・`(A)`・`調味料A`・`Ａ` のどれで書かれていても同じグループになる。
        /// </summary>
        public static StepTextResult ExpandGroups(string instruction, IReadOnlyList<Ingredient> ingredients)
        {
            var text = instruction ?? string.Empty;
            if (text.Length == 0 || ingredients == null || ingredients.Count == 0)
            {
                return new StepTextResult(text, Array.Empty<string>());
            }

            var groups = BuildGroups(ingredients);
            if (groups.Count == 0)
            {
                return new StepTextResult(text, Array.Empty<string>());
            }

            var notes = new List<string>();
            foreach (var key in ReferencedGroupKeys(text))
            {
                // 参照が在っても材料側に無ければ何も添えない。
                if (groups.TryGetValue(key, out var group))
                {
                    notes.Add(FormatNote(key, group));
                }
            }

            return new StepTextResult(text, notes);
        }

        /// <summary>説明の中のグループ参照を、出てきた順・重複なしの正規化済みの鍵で返す。</summary>
        private static List<string> ReferencedGroupKeys(string text)
        {
            var hits = new List<KeyValuePair<int, string>>();
            Collect(Bracketed, text, hits);
            Collect(WordLetter, text, hits);
            Collect(LetterOf, text, hits);
            Collect(BareWideLetter, text, hits);
            Collect(StandaloneWord, text, hits);

            hits.Sort((a, b) => a.Key.CompareTo(b.Key));

            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hit in hits)
            {
                var key = NormalizeGroupKey(hit.Value);
                if (key.Length > 0 && seen.Add(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        // ---------------------------------------------------------------- ingredients_used の推定

        /// <summary>
        /// 短い材料名（正規化してこの字数以下）は「完全な語としての出現」にだけ当てる。
        /// 「油」が「ごま油」「油揚げ」に当たるのを防ぐため。
        /// </summary>
        public const int ShortNameLength = 2;

        /// <summary>
        /// 短い名前の前に来てよい仮名（助詞・接続の仮名）。これ以外の仮名が前に在ったら
        /// 複合語の一部とみなして当てない——「ごま油」「ひまわり油」「なたね油」を弾く。
        /// </summary>
        private const string ParticleKana = "をにはがでともやへのて";

        /// <summary>
        /// 工程の説明から「この工程で使う材料」を推定する（<c>ingredients_used</c> が空のとき用）。
        ///
        /// 拾うのは2つ:
        ///   1. 説明の文に**名前が出てくる**材料（長い名前から先に照合し、当たった所は塗り潰す）
        ///   2. `(A)`・`調味料B` などの**グループ参照**に属する材料（照合は <see cref="ExpandGroups"/> と同じ）
        ///
        /// 正規化は全角→半角・空白の除去・材料名の「（…）」の除去だけ。同義の揺れ（「しょうゆ」と
        /// 「醤油」）は扱わない——辞書を持たずに当てにいくと、外れ方が説明できなくなる。
        ///
        /// 返す順はレシピの材料の並び順。名前は材料に書かれたまま（板の行の鍵に使うため）。
        /// </summary>
        public static IReadOnlyList<string> InferIngredientsUsed(
            string instruction, IReadOnlyList<Ingredient> ingredients)
        {
            if (ingredients == null || ingredients.Count == 0)
            {
                return Array.Empty<string>();
            }

            var text = NormalizeText(instruction ?? string.Empty);
            var chosen = new HashSet<string>(StringComparer.Ordinal);

            if (text.Length > 0)
            {
                MarkByName(text, ingredients, chosen);
                MarkByGroup(text, ingredients, chosen);
            }

            if (chosen.Count == 0)
            {
                return Array.Empty<string>();
            }

            // レシピの並び順で返す（同じ名前が2行あっても1つ）。
            var result = new List<string>();
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ingredient in ingredients)
            {
                if (ingredient != null && chosen.Contains(ingredient.Name) && emitted.Add(ingredient.Name))
                {
                    result.Add(ingredient.Name);
                }
            }

            return result;
        }

        /// <summary>名前が文に出てくる材料を拾う。長い名前から当て、当たった所は塗り潰す。</summary>
        private static void MarkByName(
            string text, IReadOnlyList<Ingredient> ingredients, HashSet<string> chosen)
        {
            // 塗り潰しの跡。長い名前が先に取った所へ短い名前を当てない
            //（「ごま油」が在れば、その中の「油」は当たらない）。
            var taken = new bool[text.Length];

            var ordered = new List<Ingredient>();
            foreach (var ingredient in ingredients)
            {
                if (ingredient != null && ingredient.Name.Length > 0)
                {
                    ordered.Add(ingredient);
                }
            }

            ordered.Sort((a, b) => NormalizeName(b.Name).Length.CompareTo(NormalizeName(a.Name).Length));

            foreach (var ingredient in ordered)
            {
                var name = NormalizeName(ingredient.Name);
                if (name.Length == 0 || chosen.Contains(ingredient.Name))
                {
                    continue;
                }

                if (FindOccurrence(text, name, taken, out var at))
                {
                    for (var i = at; i < at + name.Length; i++)
                    {
                        taken[i] = true;
                    }

                    chosen.Add(ingredient.Name);
                }
            }
        }

        /// <summary>`(A)` などの参照に属する材料をまとめて拾う。</summary>
        private static void MarkByGroup(
            string text, IReadOnlyList<Ingredient> ingredients, HashSet<string> chosen)
        {
            var keys = ReferencedGroupKeys(text);
            if (keys.Count == 0)
            {
                return;
            }

            var groups = BuildGroups(ingredients);
            foreach (var key in keys)
            {
                if (!groups.TryGetValue(key, out var group))
                {
                    continue;
                }

                foreach (var item in group.Items)
                {
                    chosen.Add(item.Name);
                }
            }
        }

        /// <summary>塗り潰されていない所で名前を探す。短い名前は語として立っている所だけ。</summary>
        private static bool FindOccurrence(string text, string name, bool[] taken, out int at)
        {
            at = -1;

            for (var start = text.IndexOf(name, StringComparison.Ordinal);
                 start >= 0;
                 start = text.IndexOf(name, start + 1, StringComparison.Ordinal))
            {
                var end = start + name.Length;

                var overlaps = false;
                for (var i = start; i < end; i++)
                {
                    if (taken[i])
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                {
                    continue;
                }

                if (name.Length > ShortNameLength || IsWholeWord(text, start, end))
                {
                    at = start;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 短い名前が「語として」出ているか。
        ///
        /// 後ろは仮名なら可（「油を」の「を」は助詞）。漢字・カタカナ・英数が続いたら複合語
        /// （「油揚げ」「水菜」）なので採らない。前は助詞の仮名か句読点・空白・文頭だけ——
        /// 「ごま油」「ひまわり油」の類は前の仮名が名詞の一部なので、そこで切る。
        /// </summary>
        private static bool IsWholeWord(string text, int start, int end)
        {
            if (start > 0)
            {
                var before = text[start - 1];
                if (IsCompoundChar(before))
                {
                    return false;
                }

                if (IsKana(before) && ParticleKana.IndexOf(before) < 0)
                {
                    return false;
                }
            }

            return end >= text.Length || !IsCompoundChar(text[end]);
        }

        /// <summary>複合語を作る字（漢字・カタカナ・英数・々・ー）。仮名は含めない。</summary>
        private static bool IsCompoundChar(char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                return true;
            }

            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
            {
                return true;
            }

            if (ch == '々' || ch == 'ー')
            {
                return true;
            }

            // カタカナ（ー は上で見た）と CJK 統合漢字。
            return (ch >= 'ァ' && ch <= 'ヶ') || (ch >= '一' && ch <= '鿿');
        }

        private static bool IsKana(char ch) => ch >= 'ぁ' && ch <= 'ん';

        /// <summary>説明の側の正規化。全角→半角・大文字化・空白の除去（字の並びだけを残す）。</summary>
        private static string NormalizeText(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                var c = ToHalfWidth(ch);
                if (!char.IsWhiteSpace(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 材料名の側の正規化。説明と同じ扱いに加えて「（青い部分）」のような添え書きを落とす
        /// ——添え書きは文の側には出てこない。
        /// </summary>
        public static string NormalizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            var depth = 0;

            foreach (var ch in raw)
            {
                var c = ToHalfWidth(ch);

                if (c == '(' || c == '[' || c == '【' || c == '〔')
                {
                    depth++;
                    continue;
                }

                if (c == ')' || c == ']' || c == '】' || c == '〕')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }

                    continue;
                }

                if (depth > 0 || char.IsWhiteSpace(c))
                {
                    continue;
                }

                sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString();
        }

        private static void Collect(Regex pattern, string text, List<KeyValuePair<int, string>> hits)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var capture = match.Groups[1];
                hits.Add(new KeyValuePair<int, string>(capture.Index, capture.Value));
            }
        }

        /// <summary>材料をグループごとに束ねる（レシピの並び順を保つ）。鍵は正規化済み。</summary>
        private static Dictionary<string, GroupEntry> BuildGroups(IReadOnlyList<Ingredient> ingredients)
        {
            var groups = new Dictionary<string, GroupEntry>(StringComparer.Ordinal);
            foreach (var ingredient in ingredients)
            {
                if (ingredient == null)
                {
                    continue;
                }

                var key = NormalizeGroupKey(ingredient.Group);
                if (key.Length == 0)
                {
                    continue; // group が空の材料はグループを作らない。
                }

                if (!groups.TryGetValue(key, out var entry))
                {
                    entry = new GroupEntry(ingredient.Group.Trim());
                    groups[key] = entry;
                }

                entry.Items.Add(ingredient);
            }

            return groups;
        }

        /// <summary>`(B)＝しょうゆ 大さじ1・みりん 大さじ1` の1行を作る。</summary>
        private static string FormatNote(string key, GroupEntry group)
        {
            var sb = new StringBuilder();
            sb.Append(Label(key, group.Display)).Append('＝');

            for (var i = 0; i < group.Items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('・');
                }

                var item = group.Items[i];
                sb.Append(item.Name);

                var amount = (item.Qty ?? string.Empty) + (item.Unit ?? string.Empty);
                if (amount.Length > 0)
                {
                    sb.Append(' ').Append(amount);
                }
            }

            return sb.ToString();
        }

        /// <summary>英字のグループは括弧つきで（本文の書き方に合わせる）、語のグループはそのまま。</summary>
        private static string Label(string key, string display)
        {
            if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            {
                return "(" + key + ")";
            }

            return string.IsNullOrEmpty(display) ? key : display;
        }

        /// <summary>
        /// グループ名の表記揺れを1つの鍵に潰す。全角→半角・空白と括弧を落とす・英字は大文字・
        /// 「調味料A」の語を剥がす（剥がして空になる語そのものはそのまま鍵にする）。
        /// </summary>
        public static string NormalizeGroupKey(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                var c = ToHalfWidth(ch);
                if (char.IsWhiteSpace(c) || IsBracket(c))
                {
                    continue;
                }

                sb.Append(char.ToUpperInvariant(c));
            }

            var text = sb.ToString();
            if (text.Length == 0)
            {
                return string.Empty;
            }

            foreach (var word in GroupWords)
            {
                if (text.Length > word.Length && text.StartsWith(word, StringComparison.Ordinal))
                {
                    return text.Substring(word.Length);
                }
            }

            foreach (var suffix in new[] { "の材料", "の調味料" })
            {
                if (text.Length > suffix.Length && text.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return text.Substring(0, text.Length - suffix.Length);
                }
            }

            return text;
        }

        /// <summary>全角の ASCII（Ａ→A・（→( など）と全角空白を半角に落とす。</summary>
        private static char ToHalfWidth(char ch)
        {
            if (ch >= '！' && ch <= '～')
            {
                return (char)(ch - 0xFEE0);
            }

            return ch == '　' ? ' ' : ch;
        }

        private static bool IsBracket(char ch)
        {
            switch (ch)
            {
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case '【': // 【
                case '】': // 】
                case '〔': // 〔
                case '〕': // 〕
                    return true;
                default:
                    return false;
            }
        }

        private sealed class GroupEntry
        {
            public GroupEntry(string display)
            {
                Display = display;
            }

            /// <summary>材料側に書かれていたままのグループ名（語のグループの見出しに使う）。</summary>
            public string Display { get; }

            public List<Ingredient> Items { get; } = new List<Ingredient>();
        }
    }
}
