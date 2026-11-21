using System;
using System.Collections.Generic;

namespace LifeAstroApi
{
    /// <summary>
    /// Base class of the generated parameter classes (<c>PanchangBasicParams</c>,
    /// <c>ChartPlanetsParams</c>, ... in <c>LifeAstroApi.Params</c>): one per method. Required values are
    /// constructor parameters, optional ones settable properties. <see cref="ToPath"/> and
    /// <see cref="ToQuery"/> show exactly what a call sends; <see cref="ToString"/> is the path and query
    /// string, usable as a cache key.
    /// </summary>
    /// <remarks>
    /// A parameter object is not thread-safe while you set its properties; build one per call, or do
    /// not change it after sharing it.
    /// </remarks>
    public abstract class LifeAstroParams
    {
        private protected LifeAstroParams()
        {
        }

        /// <summary>The request path, with the path parameters filled in and percent-encoded.</summary>
        /// <returns>The path, e.g. <c>/v1/chart/divisional/D9</c>.</returns>
        public abstract string ToPath();

        /// <summary>
        /// The query parameters this sends, by wire name, sorted ordinally, as wire text: numbers as
        /// JavaScript writes them, booleans <c>true</c> / <c>false</c>, enums as their codes. Unset values
        /// are left out; a moment's <c>date</c> defaults to today in its <c>tz</c>.
        /// </summary>
        /// <returns>A new dictionary.</returns>
        public IReadOnlyDictionary<string, string> ToQuery()
        {
            var query = new SortedDictionary<string, string>(StringComparer.Ordinal);
            WriteQuery(new QueryWriter(query));
            return query;
        }

        /// <summary>The path and the encoded query string, e.g. <c>/v1/panchang/basic?date=2027-01-15&amp;lat=19.076&amp;...</c>.</summary>
        /// <returns>What this sends after the base URL.</returns>
        public override string ToString() => ToPath() + QueryString.Encode(ToQuery());

        private protected abstract void WriteQuery(QueryWriter query);
    }

    /// <summary>Writes wire values; null and empty values are skipped, 0 and false are kept.</summary>
    internal readonly struct QueryWriter
    {
        private readonly IDictionary<string, string> _query;

        internal QueryWriter(IDictionary<string, string> query)
        {
            _query = query;
        }

        internal void Put(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _query[key] = value!;
            }
        }

        internal void Put(string key, double? value)
        {
            if (value.HasValue)
            {
                _query[key] = QueryString.FormatNumber(value.Value);
            }
        }

        internal void Put(string key, int? value)
        {
            if (value.HasValue)
            {
                _query[key] = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        internal void Put(string key, bool? value)
        {
            if (value.HasValue)
            {
                _query[key] = value.Value ? "true" : "false";
            }
        }

        internal void Put(string key, Lang? value)
        {
            if (value.HasValue)
            {
                _query[key] = value.Value.ToCode();
            }
        }

        internal void Put(string key, Ayanamsa? value)
        {
            if (value.HasValue)
            {
                _query[key] = value.Value.ToWireValue();
            }
        }

        internal void Put(string key, HouseSystem? value)
        {
            if (value.HasValue)
            {
                _query[key] = value.Value.ToWireValue();
            }
        }

        internal void Put(string key, LifeAstroDate? value)
        {
            if (value.HasValue && !value.Value.IsEmpty)
            {
                _query[key] = value.Value.Value!;
            }
        }

        internal void Put(string key, LifeAstroTime? value)
        {
            if (value.HasValue && !value.Value.IsEmpty)
            {
                _query[key] = value.Value.Value!;
            }
        }

        /// <summary>Copies a birth onto prefixed wire names: <c>boy.date</c>, <c>boy.time</c>, ...</summary>
        internal void PutBirth(string prefix, BirthInput? birth)
        {
            if (birth == null)
            {
                return;
            }

            Put(prefix + "lat", birth.Lat);
            Put(prefix + "lon", birth.Lon);
            Put(prefix + "date", birth.Date);
            Put(prefix + "time", birth.Time);
            Put(prefix + "tz", birth.Tz);
        }

        /// <summary>The birth place as unprefixed <c>lat</c> / <c>lon</c>.</summary>
        internal void PutPlace(BirthInput? birth)
        {
            if (birth == null)
            {
                return;
            }

            Put("lat", birth.Lat);
            Put("lon", birth.Lon);
        }
    }
}
