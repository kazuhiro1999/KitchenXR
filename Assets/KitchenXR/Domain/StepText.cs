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

            // 参照を出てきた順に拾う（同じグループが2度出ても行は1本）。
            var hits = new List<KeyValuePair<int, string>>();
            Collect(Bracketed, text, hits);
            Collect(WordLetter, text, hits);
            Collect(LetterOf, text, hits);
            Collect(BareWideLetter, text, hits);
            Collect(StandaloneWord, text, hits);
            if (hits.Count == 0)
            {
                return new StepTextResult(text, Array.Empty<string>());
            }

            hits.Sort((a, b) => a.Key.CompareTo(b.Key));

            var notes = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hit in hits)
            {
                var key = NormalizeGroupKey(hit.Value);
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                // 参照が在っても材料側に無ければ何も添えない。
                if (groups.TryGetValue(key, out var group))
                {
                    notes.Add(FormatNote(key, group));
                }
            }

            return new StepTextResult(text, notes);
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
