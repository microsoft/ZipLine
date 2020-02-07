using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Algorithm.ZipLineClustering
{
    [DebuggerDisplay("Item: \"{Content}\" {Id} [{TextLength}]")]
    public class ClusterItem
    {
        private bool m_initPending = false;

        [JsonProperty]
        public string Id { get; protected set; }

        private List<int> m_tokenIDs;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        protected List<int> TokenIDs // used to persist the token IDs cheaper than serializing the TokenIndex.
        {
            get { return this.m_tokenIDs; }
            set { this.m_tokenIDs = value; }
        }

        [JsonIgnore]
        public TokenIndex TokenIndex { get; private set; } = new TokenIndex();

        [JsonIgnore]
        public int TextLength { get; private set; }

        [JsonIgnore]
        public string Content { get; protected set; }

        [JsonIgnore]
        public string Hash { get; private set; }

        [JsonIgnore]
        public bool PreserveHash { get; set; } = true;

        [JsonProperty("Hash", DefaultValueHandling = DefaultValueHandling.Ignore)]
        private string PreservedHash
        {
            get
            {
                return this.PreserveHash ? this.Hash : null;
            }
            set
            {
                this.Hash = this.Hash ?? value;
            }
        }

        [JsonIgnore]
        public float Affinity { get; set; }

        [JsonConstructor]
        protected ClusterItem()
        {
            this.m_initPending = true;
        }

        public ClusterItem(string id, string content, ClusteringVocabulary vocabulary)
        {
            this.Id = id;
            this.TextLength = content?.Length ?? 0;
            this.TokenIndex = vocabulary.GetTokens(content);
            this.Content = content;
            this.Hash = Clustering.PlatformGetStringHash(content);
        }

        public virtual bool InitIfPending(ClusteringVocabulary vocabulary)
        {
            if (this.m_initPending && this.TokenIDs != null)
            {
                this.m_initPending = false;
                foreach (Token token in this.TokenIDs.Select(tid => vocabulary.GetToken(tid)))
                {
                    this.TokenIndex.Append(token);
                }

                return true;
            }

            return false;
        }

        internal void OnAdded()
        {
            this.itemTokenPlaceMap = null;
        }

        Dictionary<int, int[]> itemTokenPlaceMap = null;

        /// <summary>
        /// returns a Dictionary keyed on the token IDs in this item
        /// with values that are a set if indexes the token is found
        /// </summary>
        internal Dictionary<int, int[]> GetTokenPlacesMap()
        {
            if (this.itemTokenPlaceMap == null)
            {
                lock (this)
                {
                    if (this.itemTokenPlaceMap == null)
                    {
                        var itemTokenMap = new Dictionary<int, List<int>>();
                        for (int i = 0; i < this.TokenIndex.Tokens.Length; i++)
                        {
                            Token token = this.TokenIndex.Tokens[i];
                            List<int> tokenPlaces;
                            if (!itemTokenMap.TryGetValue(token.Id, out tokenPlaces))
                            {
                                tokenPlaces = new List<int> { i };
                                itemTokenMap[token.Id] = tokenPlaces;
                            }
                            else
                            {
                                tokenPlaces.Add(i);
                            }
                        }

                        this.itemTokenPlaceMap = new Dictionary<int, int[]>();
                        foreach (KeyValuePair<int, List<int>> kv in itemTokenMap)
                        {
                            this.itemTokenPlaceMap.Add(kv.Key, kv.Value.ToArray());
                        }
                    }
                }
            }

            return this.itemTokenPlaceMap;
        }
    }

    public class ZipLineItemShim : ClusterItem
    {
        [JsonConstructor]
        protected ZipLineItemShim()
        {
        }

        public ZipLineItemShim(string id)
        {
            this.Id = id;
        }
    }
}