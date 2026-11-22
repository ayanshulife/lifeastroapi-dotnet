using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LifeAstroApi.Pdf
{
    /// <summary>
    /// A regular expression of the templates, already translated to a .NET pattern that matches what
    /// JavaScript matches (tool/lib/js-regex.mjs). Immutable and safe to share between threads.
    /// </summary>
    internal sealed class JsRegex
    {
        private readonly Regex _regex;
        private readonly bool _global;
        private readonly string _required;

        /// <param name="pattern">The .NET pattern.</param>
        /// <param name="global">The <c>g</c> flag.</param>
        /// <param name="required">A literal that every match contains, or empty.</param>
        internal JsRegex(string pattern, bool global, string required)
        {
            // Compiled only: RegexOptions.IgnoreCase and ECMAScript both differ from JavaScript.
            _regex = new Regex(pattern, RegexOptions.Compiled);
            _global = global;
            _required = required;
        }

        private bool CannotMatch(string text) =>
            _required.Length > 0 && text.IndexOf(_required, StringComparison.Ordinal) < 0;

        internal string Replace(string text, string replacement)
        {
            if (CannotMatch(text))
            {
                return text;
            }

            return _global ? _regex.Replace(text, replacement) : _regex.Replace(text, replacement, 1);
        }

        /// <summary>The callback gets the match, its groups (undefined when they did not match), the index and the text.</summary>
        internal string Replace(string text, Js callback)
        {
            if (CannotMatch(text))
            {
                return text;
            }

            StringBuilder? result = null;
            int copied = 0;
            for (Match match = _regex.Match(text); match.Success; match = match.NextMatch())
            {
                int groups = match.Groups.Count;
                var args = new Js[groups + 2];
                for (int i = 0; i < groups; i++)
                {
                    args[i] = GroupValue(match.Groups[i]);
                }

                args[groups] = Js.N(match.Index);
                args[groups + 1] = Js.S(text);
                result ??= new StringBuilder(text.Length + 16);
                result.Append(text, copied, match.Index - copied).Append(callback.Call(args).Str());
                copied = match.Index + match.Length;
                if (!_global)
                {
                    break;
                }
            }

            return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
        }

        /// <summary>The pieces between the matches, the empty ones at both ends too, as in JavaScript.</summary>
        internal List<Js> Split(string text)
        {
            var pieces = new List<Js>();
            int from = 0;
            for (Match match = _regex.Match(text); match.Success; match = match.NextMatch())
            {
                if (match.Length == 0 || match.Groups.Count > 1)
                {
                    throw new InvalidOperationException("split() is translated only for patterns without groups that match some text");
                }

                pieces.Add(Js.S(text.Substring(from, match.Index - from)));
                from = match.Index + match.Length;
            }

            pieces.Add(Js.S(text.Substring(from)));
            return pieces;
        }

        internal bool IsMatch(string text)
        {
            RequireStateless();
            return !CannotMatch(text) && _regex.IsMatch(text);
        }

        internal Js Exec(string text)
        {
            RequireStateless();
            if (CannotMatch(text))
            {
                return Js.Null;
            }

            Match match = _regex.Match(text);
            if (!match.Success)
            {
                return Js.Null;
            }

            var values = new Js[match.Groups.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = GroupValue(match.Groups[i]);
            }

            return Js.Arr(values);
        }

        private static Js GroupValue(Group group) => group.Success ? Js.S(group.Value) : Js.Undefined;

        private void RequireStateless()
        {
            // test() and exec() of a global regex keep a lastIndex in JavaScript; these are shared and stateless.
            if (_global)
            {
                throw new InvalidOperationException("test() and exec() are not translated for global regular expressions");
            }
        }

        public override string ToString() => "/" + _regex + "/" + (_global ? "g" : string.Empty);
    }
}
