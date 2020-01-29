using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace Algorithm.ZipLineClustering
{
    public class ClusteringVocabulary
    {
        [JsonIgnore]
        public ClusteringConfig Config { get; set; }


        private int m_maxTokenId = 1;
        private Dictionary<string, Token> m_textToToken = new Dictionary<string, Token>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        protected int MaxTokenId
        {
            get
            {
                if (this.m_maxTokenId <= 0)
                {
                    if (this.TextToToken.Any())
                        this.m_maxTokenId = this.TextToToken.Values.Max(tok => tok.Id) + 1;
                }

                return this.m_maxTokenId;
            }

            private set { this.m_maxTokenId = value; }
        }

        [JsonProperty]
        protected Dictionary<string, Token> TextToToken
        {
            get { return this.m_textToToken; }
            private set
            {
                this.m_textToToken = value ?? new Dictionary<string, Token>(StringComparer.OrdinalIgnoreCase);
                this.IdToToken.Clear();
                this.IdToText.Clear();
                this.m_maxTokenId = 0;

                if (this.m_textToToken.Any())
                {
                    foreach (KeyValuePair<string, Token> txtToken in this.m_textToToken)
                    {
                        Token token = txtToken.Value;
                        this.IdToToken.Add(token.Id, token);
                        this.IdToText.Add(token.Id, txtToken.Key);
                        this.m_maxTokenId = Math.Max(this.m_maxTokenId, token.Id);
                    }

                    this.m_maxTokenId += 1;
                }
            }
        }

        [JsonIgnore]
        protected Dictionary<int, Token> IdToToken { get; private set; } = new Dictionary<int, Token>();

        [JsonIgnore]
        protected Dictionary<int, string> IdToText { get; private set; } = new Dictionary<int, string>();


        [JsonConstructor]
        protected ClusteringVocabulary()
        {
            this.m_maxTokenId = -1;
        }

        public ClusteringVocabulary(ClusteringConfig config) : this()
        {
            this.Config = config;
        }

        public TokenIndex GetTokens(IEnumerable<int> tokenIds)
        {
            IEnumerable<Token> tokens = tokenIds.Select(tid => this.IdToToken[tid]);
            return new TokenIndex(tokens);
        }

        public string GetText(int tokenId)
        {
            string txt = null;
            if (!this.IdToText.TryGetValue(tokenId, out txt))
            {
                return (tokenId < 0) ? "*" : "??";
            }

            return txt;
        }

        public TokenIndex GetTokens(string content)
        {
            List<string> tokens = this.Config.GetTokens(content);

            var result = new TokenIndex();

            lock (this.TextToToken)
            {
                foreach (string tokenText in tokens)
                {
                    Token token;
                    if (!this.TextToToken.TryGetValue(tokenText, out token))
                    {
                        int id = ++this.MaxTokenId;
                        token = new Token(id, tokenText);

                        token.Weight = this.GetTokenWeight(tokenText);

                        this.TextToToken.Add(tokenText, token);
                        this.IdToToken.Add(id, token);
                        this.IdToText.Add(id, tokenText);
                    }

                    result.Append(token);
                }
            }

            return result;
        }

        internal void UseWildcard(int wildcard, string wildcardText)
        {
            lock (this.IdToToken)
            {
                if (!this.IdToToken.ContainsKey(wildcard))
                {
                    this.IdToToken.Add(wildcard, new Token(wildcard, wildcardText));
                }
            }
        }

        private Dictionary<Regex, float> weightedTokens = null;

        private float GetTokenWeight(string tokenText)
        {
            float? weight = null;
            if (this.Config.WeightedTokens?.Any() == true)
            {
                if (this.weightedTokens == null)
                {
                    this.weightedTokens = new Dictionary<Regex, float>();
                    foreach (KeyValuePair<string, float> matchWeight in this.Config.WeightedTokens)
                    {
                        Regex regex = ClusteringConfig.PlatformRegexInit("^" + matchWeight.Key.TrimStart('^').TrimEnd('$') + "$", false, TimeSpan.FromMilliseconds(100));
                        this.weightedTokens[regex] = matchWeight.Value;
                    }
                }

                try
                {
                    foreach (KeyValuePair<Regex, float> matchWeight in this.weightedTokens)
                    {
                        if (matchWeight.Key.IsMatch(tokenText))
                        {
                            if (weight.HasValue)
                                weight = Math.Max(weight.Value, matchWeight.Value);
                            else
                                weight = matchWeight.Value;
                        }
                    }
                    if (weight.HasValue)
                    {
                        weight *= (float)Math.Max(1, Math.Log(tokenText.Length));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    Debug.WriteLine($"'{tokenText}' token weight evaluation timed out, assigning weight=2.0");
                    weight = 2.0f;
                }
            }

            return weight.GetValueOrDefault(1);
        }

        public Token GetToken(int tid)
        {
            Token token;
            if (this.IdToToken.TryGetValue(tid, out token))
            {
                return token;
            }

            throw new ClusteringException(ClusteringException.FaultTypes.TokenNotFound, "Token not found: " + tid);
        }
    }
}