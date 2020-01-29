using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Algorithm.ZipLineClustering
{
    [DebuggerDisplay("Token: {Val} {Text}")]
    public class Token
    {
        [JsonIgnore]
        public int Id { get; private set; }

        [JsonIgnore]
        public int Length { get; private set; }

        [JsonIgnore]
        public float Weight { get; set; }

        [JsonProperty] // serializes in a compact manner
        protected string Val
        {
            get
            {
                return Math.Abs(1.0f - this.Weight) > 0.0001f
                  ? $"{this.Id}|{this.Length}|{this.Weight}"
                  : $"{this.Id}|{this.Length}"; // skip the Weight if it's normal
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    string[] parts = value.Split('|');
                    this.Id = int.Parse(parts[0]);
                    this.Length = int.Parse(parts[1]);
                    this.Weight = parts.Length > 2 ? float.Parse(parts[2]) : 1.0f;
                }
            }
        }

        internal string Text { get; private set; }

        public Token(int id, string text, float weight = 1.0f)
        {
            this.Length = text?.Length ?? 0;
            this.Id = id;
            this.Weight = weight;

            if (Debugger.IsAttached) this.Text = text;

        }

        public override string ToString()
        {
            return this.Text + this.Val;
        }
    }

    public class TokenIndexBase
    {

    }

    public class TokenIndex : TokenIndexBase
    {
        protected readonly ConcurrentDictionary<int, List<int>> TokenIndexes = new ConcurrentDictionary<int, List<int>>();

        private Token[] m_Tokens;

        public Token[] Tokens
        {
            get
            {
                if (this.Count <= 0) return new Token[0];

                if (this.m_Tokens.Length != this.Count)
                {
                    Array.Resize(ref this.m_Tokens, this.Count);
                }
                return this.m_Tokens;
            }

            protected set
            {
                this.m_count = 0;
                this.m_Tokens = new Token[value?.Length ?? 100];
                foreach (Token token in value)
                {
                    this.Append(token);
                }
            }
        }

        public IEnumerator<int> GetTokenIds() => this.TokenIndexes.Keys.GetEnumerator();

        /// <summary>
        /// All the tokens counted, including repetitions
        /// </summary>
        public int TotalCount => this.m_count;

        private int m_count = -1;

        /// <summary>
        /// Distinct tokens count
        /// </summary>
        public int Count => this.m_count;

        public TokenIndex()
        {
            this.m_Tokens = new Token[100];
        }

        public TokenIndex(IEnumerable<Token> tokens)
        {
            this.m_Tokens = tokens.ToArray();
            this.m_count = this.m_Tokens.Length;
        }

        public void Append(Token token)
        {
            int tokenIndex = Interlocked.Increment(ref this.m_count);
            this.Add(token, tokenIndex);
        }

        private void Add(Token token, int atIndex)
        {
            List<int> indexes;
            if (!this.TokenIndexes.TryGetValue(token.Id, out indexes))
            {
                indexes = new List<int>();
                this.TokenIndexes.TryAdd(token.Id, indexes);

                if (!this.TokenIndexes.TryGetValue(token.Id, out indexes))
                {
                    throw new ClusteringException(ClusteringException.FaultTypes.InconsistentState, "Unexpected inconsistent state");
                }

                indexes.Add(atIndex);
            }

            if (this.m_Tokens != null)
            {
                if (this.m_Tokens.Length <= atIndex)
                {
                    Array.Resize(ref this.m_Tokens, Math.Min(this.m_Tokens.Length + 5, Math.Max(4000, this.m_Tokens.Length * 110 / 100)));
                }

                this.m_Tokens[atIndex] = token;
            }
        }

        public bool Contains(Token token) => this.Contains(token.Id);

        public bool Contains(int tokenId)
        {
            List<int> indexes = this.IndexOfInternal(tokenId);
            return indexes != null;
        }

        public int CountOf(Token token) => this.CountOf(token.Id);

        public int CountOf(int tokenId)
        {
            List<int> indexes = this.IndexOfInternal(tokenId);
            return indexes?.Count ?? 0;
        }

        public int IndexOf(Token token, int startingFromIndex = -1) => this.IndexOf(token.Id, startingFromIndex);

        public int IndexOf(int tokenId, int startingFromIndex = -1)
        {
            List<int> indexes = this.IndexOfInternal(tokenId);

            if (indexes != null)
            {
                if (startingFromIndex < 0)
                    return indexes.Min();

                if (indexes.Last() > startingFromIndex)
                {
                    int min = -1;
                    for (int i = 0; i < indexes.Count; i++)
                    {
                        if (indexes[i] > startingFromIndex) min = indexes[i];
                    }
                    return min;
                }
            }

            return -1;
        }

        protected List<int> IndexOfInternal(int tokenId)
        {
            List<int> indexes;
            if (this.TokenIndexes.TryGetValue(tokenId, out indexes))
            {
                return indexes;
            }

            return null;
        }
    }

    public class ClusterTokenIndex : TokenIndexBase
    {
        [JsonProperty]
        protected List<Dictionary<int, CountedValue<Token>>> TokenParts { get; private set; }

        [JsonIgnore]
        public int Count => this.TokenParts.Sum(part => part.Count);

        [JsonIgnore]
        public int PartsCount => this.TokenParts.Count;

        protected ClusterTokenIndex()
        {
            this.TokenParts = new List<Dictionary<int, CountedValue<Token>>>();
        }

        public ClusterTokenIndex(int partsCount = 3)
        {
            if (partsCount < 1) partsCount = 1;
            this.TokenParts = new List<Dictionary<int, CountedValue<Token>>>(Enumerable.Range(0, partsCount).Select(r => new Dictionary<int, CountedValue<Token>>()));
        }

        // returns the count of newly added tokens
        public int Add(TokenIndex tokenIdx)
        {
            return (int)this.ForParts(tokenIdx.Tokens,
                (tokens, tokenIndex, partIndex) =>
                {
                    CountedValue<Token> cTok;
                    if (this.TokenParts[partIndex].TryGetValue(tokens[tokenIndex].Id, out cTok))
                    {
                        cTok.Count++;
                        return 0;
                    }

                    this.TokenParts[partIndex][tokens[tokenIndex].Id] = new CountedValue<Token>(tokens[tokenIndex]) { Count = 1 };
                    return 1;
                });
        }

        public float Overlap(TokenIndex tokenIdx, float minAffinity = float.MaxValue)
        {
            int ptCount = this.Count;

            bool useMin = Math.Min(ptCount, tokenIdx.Count) >= this.PartsCount * 10; // if too few tokens then the output for Min would be very jittery

            if (!useMin)
            {
                float byCount = ptCount != tokenIdx.Count ? Math.Min(ptCount, tokenIdx.Count) / (float)Math.Max(ptCount, tokenIdx.Count) : 1.0f;
                if (byCount < minAffinity || (tokenIdx.Count == 0 && ptCount == 0)) return byCount;

                return (byCount + tokenIdx.Tokens.Count(t => this.TokenParts.Any(pt => pt.ContainsKey(t.Id))) / tokenIdx.Tokens.Count()) / 2;
            }
            else
            {
                float[] partPresent = new float[this.TokenParts.Count];
                float[] partTotal = new float[this.TokenParts.Count];
                this.ForParts(tokenIdx.Tokens.ToArray(),
                    (tokens, tokenIndex, partIndex) =>
                    {
                        Token token = tokens[tokenIndex];
                        bool partContainsToken = this.TokenParts[partIndex].ContainsKey(token.Id);
                        partTotal[partIndex] += token.Weight * tokenIdx.CountOf(token.Id);
                        if (partContainsToken)
                        {
                            partPresent[partIndex] += token.Weight * this.TokenParts[partIndex][token.Id].Count;
                        }
                        return token.Weight;
                    });

                double rootMeanSqare = 0;

                for (int p = 0; p < partTotal.Length; p++)
                {
                    double partValue = partTotal[p] > 0 ? partPresent[p] / partTotal[p] : 0;
                    rootMeanSqare += partValue * partValue;
                }
                rootMeanSqare /= partTotal.Length;
                rootMeanSqare = Math.Sqrt(rootMeanSqare);
                return (float)rootMeanSqare;
            }
        }

        protected float? ForParts(Token[] withTokens, Func<Token[], int, int, float?> forIndexPart)
        {
            float sumWeight = 0;
            for (int part = 0; part < this.TokenParts.Count; part++)
            {
                int partStart = Math.Max(0, (withTokens.Length * part / this.TokenParts.Count) - withTokens.Length / (this.TokenParts.Count * 10));
                int partEnd = Math.Min(withTokens.Length - 1, (withTokens.Length * (part + 1) / this.TokenParts.Count) + withTokens.Length / (this.TokenParts.Count * 10));

                for (int i = partStart; i <= partEnd; i++)
                {
                    float? fip = forIndexPart(withTokens, i, part);
                    if (fip.HasValue)
                    {
                        sumWeight += fip.Value;
                    }
                    else return null;
                }
            }

            return sumWeight;
        }
    }
}
