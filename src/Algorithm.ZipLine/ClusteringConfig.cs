using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Algorithm.ZipLineClustering
{
    public class ClusteringConfig
    {
        public const string StandardLexerRegex = @"(\p{L}|\p{N})+";  // matches non-empty letter+number sequences

        public static Func<string, bool, TimeSpan?, Regex> PlatformRegexInit { get; set; } =
          (regexPattern, ignoreCase, timeout) =>
          {
              if (timeout != null)
                  return new Regex(regexPattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), timeout.Value);

              return new Regex(regexPattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));
          };

        [JsonProperty]
        public string LexerRegex { get; set; } = StandardLexerRegex;

        private Regex m_tokensRegex = null;
        /// <summary>
        /// Used to split a string into "words" or "tokens".
        /// Note: set the regex in the calling assembly with Compiled option, PCL doesn't have this option
        /// </summary>
        [JsonIgnore]
        public Regex TokensRegex
        {
            get { return (this.m_tokensRegex = this.m_tokensRegex ?? new Regex(this.LexerRegex)); }
            set { this.m_tokensRegex = value; }
        }

        public int MaxDegreeOfParallelism { get; set; } = 4;

        public float MinClusterAffinity { get; set; } = 0.75f; // if more than this many tokens common % after presence is considered - then it's a cluster item

        /// <summary>
        /// only clusters larger than this will be considered for split
        /// </summary>
        public int MinClusterSizeToSplit { get; set; } = 20;

        public Dictionary<string, float> WeightedTokens { get; set; }

        /// <summary>
        /// Additional factor for T-testing text length as a prerequisite for an item to belong to a cluster.
        /// If 1.0 then the T-test is based on the T-table.
        /// (0, 1.0) - the test is for a tighter match than normal T test
        /// (1.0, inf) - the test is more relaxed
        /// If less or equal to 0 the T-test for text length is not performed
        ///
        /// The test should be, generally speaking, more relaxed (>1.0) because single tokens could greatly change text length
        /// </summary>
        public double StDevFactorTextLength { get; internal set; } = 2.0; // ~4*StDev for >97% confidence

        /// <summary>
        /// Additional factor for T-testing token count as a prerequisite for an item to belong to a cluster.
        /// If 1.0 then the T-test is based on the T-table.
        /// (0, 1.0) - the test is for a tighter match than normal T test
        /// (1.0, inf) - the test is more relaxed
        /// If less or equal to 0 the T-test for text length is not performed
        ///
        /// The test should be, generally speaking, a little more relaxed (>1.0) so that the actual clustering algorithm takes the main decision
        /// </summary>
        public double StDevFactorTokenCount { get; internal set; } = 1.20;

        /// <summary>
        /// Additional factor for T-testing affinity as a prerequisite for an item to belong to a cluster.
        /// This test takes place after the actual clustering algorithm produces an affinity value
        /// If 1.0 then the T-test is based on the T-table.
        /// (0, 1.0) - the test is for a tighter match than normal T test
        /// (1.0, inf) - the test is more relaxed
        /// If less or equal to 0 the T-test for text length is not performed
        ///
        /// The test should be, generally speaking, set to 1.0,
        /// in which case the T test is for the calculated affinity to be within above the lower bound of the 90% confidence interval
        /// </summary>
        public double StDevFactorAffinity { get; internal set; } = 1.15;

        /// <summary>
        /// Enable/disable debug logging
        /// </summary>
        public bool LogDebug { get; internal set; } = true;

        internal Action<string> logHook = (s) => System.Diagnostics.Debug.WriteLine($" [FS]: {s}");

        /// <summary>
        /// Replace logging method that defaults to Debug.WriteLine
        /// </summary>
        public void SetLogHook(Action<string> logHook)
        {
            this.logHook = logHook;
        }

        public virtual List<string> GetTokens(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new List<string>();
            }

            return this.TokensRegex.Matches(content).OfType<Match>()
                .Select(m => m.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim('-', ':', '_', '/', '\\'))
                .SelectMany(v=>v.Split('_'))
                .Where(v => v.Length > 1)
                .ToList();
        }
    }
}