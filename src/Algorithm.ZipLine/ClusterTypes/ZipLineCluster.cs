using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Algorithm.Statistics;

namespace Algorithm.ZipLineClustering.ClusterTypes
{
    [DebuggerDisplay("[Cluster[{Items.Count}]: {LastItem.Content}")]
    public class ZipLineCluster : ClusterBase
    {
        [JsonProperty(Order = 999)]
        public TokenZipNode TokenZipRoot { get; private set; }

        [JsonIgnore]
        protected ClusteringVocabulary Vocabulary { get; private set; }

        private float? MaxScore = null;

        [JsonConstructor]
        protected ZipLineCluster() : base() { }

        public ZipLineCluster(ClusteringConfig config) : base(config)
        {
        }

        public ZipLineCluster(ClusteringConfig config, ClusteringVocabulary vocabulary) : base(config)
        {
#if DEBUG
            this.Vocabulary = vocabulary;
            this.Vocabulary?.UseWildcard(TokenZipNode.WildcardId, "*");
#endif
        }

        /// <summary>
        /// ZIP-like comparison: the cluster maintains zip chains (TokenZips) which consist of sequences of tokens that are found across several items.
        /// Getting the affinity for an unknown item checks how many of these chains are encountered in the item
        /// </summary>
        protected override float OnGetAffinity(ClusterItem item, float minAffinity)
        {
            Tuple<float, float> matchingAndTotalWeight = CalculateAffinity(item, this.TokenZipRoot, this.Config, this.Items.Count);
            return matchingAndTotalWeight.Item1 / matchingAndTotalWeight.Item2;
        }

        protected Tuple<float, float> CalculateAffinity(ClusterItem item, TokenZipNode rootZipNode, ClusteringConfig config, int itemsCount)
        {
            Dictionary<int, int[]> itemTokenPlaceMap = item.GetTokenPlacesMap();
            Debug.Assert(itemTokenPlaceMap != null);

            int encounterThreshold = (int)Math.Floor(itemsCount * config.MinClusterAffinity * config.MinClusterAffinity);
            var itemTokensFound = new HashSet<int>();
            Tuple<float, float> matchingAndTotalWeight = CalculateAffinity(0, itemTokenPlaceMap, rootZipNode, itemsCount, encounterThreshold, null, ref itemTokensFound);

            float foundWeight = 0.0f, notFoundWeight = 0.0f;
            int seqIndex = 1;
            foreach (Token token in item.TokenIndex.Tokens)
            {
                if (itemTokensFound.Contains(token.Id))
                {
                    seqIndex = 1;
                    foundWeight += 1;
                }
                else
                {
                    notFoundWeight += (float)(1 * Math.Pow(seqIndex++, 1.25));
                }
            }
            return new Tuple<float, float>(matchingAndTotalWeight.Item1 + foundWeight, matchingAndTotalWeight.Item2 + foundWeight + notFoundWeight);
        }

        protected Tuple<float, float> CalculateAffinity(int distanceToRoot, Dictionary<int, int[]> itemTokenPlaceMap, TokenZipNode tokenNode, int itemsCount, int encounterThreshold, HashSet<int> potentialPlaces, ref HashSet<int> itemTokensFound)
        {
            bool zipNodeMatches = (potentialPlaces?.Any() != false);
            if (zipNodeMatches && tokenNode.TokenId >= 0) itemTokensFound.Add(tokenNode.TokenId);

            float heightBonus = (float)Math.Log(distanceToRoot + 1);
            float tzWeight = tokenNode.TokenWeight * distanceToRoot * tokenNode.Encounters / itemsCount;

            if (tokenNode.IsLeaf)
            {
                if (zipNodeMatches)
                {
                    // a complete matching chain gets emphasized by multiplying by its heigh
                    tzWeight *= heightBonus; // added bonus for reaching the top
                    return new Tuple<float, float>(tzWeight, tzWeight);
                }
                else
                {
                    return new Tuple<float, float>(0, tzWeight);
                }
            }
            else
            {
                float matchingWeight = 0;
                float totalWeight = 0;
                foreach (TokenZipNode tzTreeChild in tokenNode.ChildNodes)
                {
                    int[] tokenPlaceMap = null;
                    bool startTokenFound = tzTreeChild.TokenId == TokenZipNode.WildcardId;
                    if (!startTokenFound)
                    {
                        for (int i = 0; i < 3; i++) // TODO: HACK for some race condition causing nullref
                        {
                            try
                            {
                                startTokenFound = itemTokenPlaceMap.TryGetValue(tzTreeChild.TokenId, out tokenPlaceMap);
                                break;
                            }
                            catch when (i < 2)
                            {
                                System.Threading.Tasks.Task.Delay(1).Wait(); // ugly hack for thread-contested itemTokenPlaceMap
                            }
                        }
                    }

                    // Say our token ID is 5 and the item has  [2,3,4,5,4,3,5,7,9] tokens.
                    // tokenPlaceMap[5] will be [3,6]
                    // this means our next token must be 4 or 7 to match anything
                    // if it's 4 then we remove the 3 from [3,6], if it's 7 - remove 6
                    // our potentialPlaces items must be present (at least some) in the tokenPlace map with an offset of DistanceToRoot
                    // ie potentialPlaces[5,6] matches  tokenPlaceMap[5+tz,DistanceToRoot]
                    HashSet<int> nextPotentialPlaces = null;
                    if (!startTokenFound)
                    {
                        if (nextPotentialPlaces == null) nextPotentialPlaces = new HashSet<int>();
                    }
                    else
                    {
                        if (tokenPlaceMap != null)
                        {
                            // if there are potential places for next tokens - take them into account
                            nextPotentialPlaces = potentialPlaces?.Any() == true
                                ? new HashSet<int>(potentialPlaces.Except(tokenPlaceMap.Select(t => t + distanceToRoot)))
                                : new HashSet<int>(tokenPlaceMap);
                        }
                    }

                    Tuple<float, float> nodeAffinity = CalculateAffinity(distanceToRoot + 1, itemTokenPlaceMap, tzTreeChild, itemsCount, encounterThreshold, nextPotentialPlaces, ref itemTokensFound);

                    if (tzTreeChild.Encounters + tzTreeChild.DistanceToRoot >= encounterThreshold)
                    {
                        if (nextPotentialPlaces?.Any() == true || tzTreeChild.TokenId == TokenZipNode.WildcardId)
                            matchingWeight += nodeAffinity.Item1;
                        totalWeight += nodeAffinity.Item2;
                    }
                }

                if (matchingWeight > 0)
                {
                    return new Tuple<float, float>(matchingWeight + tzWeight, totalWeight + tzWeight);
                }
                else if (tokenNode.Encounters + distanceToRoot >= encounterThreshold)
                {
                    tzWeight *= heightBonus;
                    return new Tuple<float, float>(tzWeight, totalWeight + tzWeight);
                }
                else
                {
                    return new Tuple<float, float>(0, 0);
                }

            }
        }

        /// <summary>
        /// TokenZips zip chains are updated when adding an item to include the item's own sequences, extend TokenZips' sequences by an extra token and remote TokenZips chains that can be consolidated
        /// </summary>
        protected override void OnAddToCluster(ClusterItem item, float affinity)
        {
            this.MaxScore = null;
            this.TokenZipRoot = this.TokenZipRoot ?? TokenZipNode.CreateRoot(item.TokenIndex.Tokens, this.Vocabulary);

            this.TokenZipRoot.Append(item.TokenIndex.Tokens.Distinct());

            this.TokenZipRoot.Compact();
        }


        public override bool InitIfPending(Clustering clustering, ClusteringVocabulary vocabulary)
        {
            this.Vocabulary = vocabulary;
            return base.InitIfPending(clustering, vocabulary);
        }
    }

    [DebuggerDisplay("{DebuggerDisplay,nq}")] // nq= no quotes
    public class TokenZipNode
    {
        public const int WildcardId = -7;
        public const int RootId = -99;

        [JsonProperty("t")]
        public int TokenId { get; private set; }

#if DEBUG
        [JsonProperty("txt", DefaultValueHandling = DefaultValueHandling.Ignore)]
        private string Text
        {
            get
            {
                string txt = null;
                if (this.IsLeaf && this.Vocabulary != null)
                {
                    TokenZipNode step = this;
                    while (step != null)
                    {
                        if (!step.IsRoot)
                            txt = (this.TokenId >= 0 ? this.Vocabulary.GetText(step.TokenId) : "*") +
                                  (string.IsNullOrEmpty(txt) ? string.Empty : " " + txt);
                        step = step.Parent;
                    }
                }
                return txt;
            }
        }

#endif
        [JsonProperty("e", DefaultValueHandling = DefaultValueHandling.Ignore)]
        private int? EncountersSerialized
        {
            get { return this.Encounters > 1 ? this.Encounters : (int?)null; }
            set { this.Encounters = value.GetValueOrDefault(1); }
        }

        [JsonIgnore]
        public int Encounters { get; set; } = 1;

        [JsonProperty("w", DefaultValueHandling = DefaultValueHandling.Ignore)]
        private float? TokenWeightSerialized
        {
            get { return Math.Abs(1 - this.TokenWeight) > 0.0001 ? this.TokenWeight : (float?)null; }
            set { this.TokenWeight = value.GetValueOrDefault(1); }
        }

        [JsonIgnore]
        public float TokenWeight { get; private set; } = 1.0f;

        private List<TokenZipNode> m_childNodes = new List<TokenZipNode>();

        [JsonProperty("c", Order = 99, DefaultValueHandling = DefaultValueHandling.Ignore)]
        private List<TokenZipNode> ChildNodesSerialized //TODO: internal serialization to string, similar to the DebuggerDisplay for improved space management
        {
            get { return (this.m_childNodes?.Any() == true) ? this.m_childNodes : null; }
            set
            {
                this.m_childNodes.Clear();
                this.ChildTokenIds.Clear();
                if (value?.Any() == true)
                {
                    foreach (TokenZipNode ct in value)
                    {
                        TokenZipNode node = this.CreateChildNode(ct.TokenId, ct.TokenWeight);
                        node.Encounters = ct.Encounters;
                        this.AppendNewNode(node);
                        if (ct.ChildNodesSerialized?.Any() == true) node.ChildNodesSerialized = ct.ChildNodesSerialized;
                    }
                }
            }
        }

        [JsonIgnore]
        public List<TokenZipNode> ChildNodes
        {
            get { return this.m_childNodes; }
        }

        [JsonIgnore]
        protected Dictionary<int, TokenZipNode> ChildTokenIds { get; } = new Dictionary<int, TokenZipNode>();

        [JsonIgnore]
        protected TokenZipNode Parent { get; set; }

        private TokenZipNode m_Root = null;
        [JsonIgnore]
        protected TokenZipNode Root
        {
            get
            {
                if (this.m_Root == null)
                {
                    this.m_Root = this;
                    while (this.m_Root.Parent != null)
                    {
                        this.m_Root = this.m_Root.Parent;
                    }
                }
                return this.m_Root;
            }
        }

        [JsonIgnore]
        public bool IsLeaf => this.DistanceToTop == 0;

        [JsonIgnore]
        public bool IsRoot => this.Parent == null;

        [JsonIgnore]
        public int DescendantsCount { get; private set; }

        [JsonIgnore]
        public int DistanceToTop { get; protected set; }

        [JsonIgnore]
        public int DistanceToRoot { get; protected set; }

        [JsonIgnore]
        public HashSet<int> AllTokenIDs
        {
            get
            {
                if (this.m_allTokenIDs == null)
                {
                    if (this.Parent == null) // is it the root node?
                    {
                        this.m_allTokenIDs = new HashSet<int>();
                    }
                }
                return this.m_allTokenIDs;
            }
        }

        private bool CheckCompact = true;
        private HashSet<int> m_allTokenIDs;

#if DEBUG
        internal ClusteringVocabulary Vocabulary { get; set; }

        protected string DebuggerDisplay
        {
            get
            {
                string path = "(+" + this.ChildNodes.Count + ")";
                TokenZipNode step = this;
                while (step?.Parent != null)
                {
                    string tokenText = step.TokenId == RootId ? "Root" : step.TokenId == WildcardId ? "*" : (this.Vocabulary?.GetText(step.TokenId) ?? step.TokenId.ToString());
                    path = "[t:" + tokenText + " e:" + step.Encounters + " w:" + step.TokenWeight.ToString("#.#") + " " + path + "]";
                    step = step.Parent;
                }
                return path;
            }
        }
#else
        protected string DebuggerDisplay{ get { return TokenId == WildcardId ? "*":TokenId.ToString(); } }
#endif

        [JsonConstructor]
        protected TokenZipNode()
        {
        }

        public static TokenZipNode CreateRoot(IEnumerable<Token> tokens, ClusteringVocabulary vocabulary = null)
        {
            var node = new TokenZipNode(WildcardId, 0, null);
#if DEBUG
            node.Vocabulary = vocabulary;
#endif
            return node;
        }

        protected virtual TokenZipNode CreateChildNode(int tokenId, float weight)
        {
            var node = new TokenZipNode(tokenId, weight, this);
            node.DistanceToRoot = this.DistanceToRoot + 1;
#if DEBUG
            node.Vocabulary = this.Vocabulary;
#endif
            return node;
        }

        protected TokenZipNode(Token token, TokenZipNode parent)
        {
            this.TokenId = token.Id;
            this.TokenWeight = token.Weight;
            this.Parent = parent;
        }

        protected TokenZipNode(int tokenId, float tokenWeight, TokenZipNode parent)
        {
            this.TokenId = tokenId;
            this.TokenWeight = tokenWeight;
            this.Parent = parent;
        }

        // important: the returned enumeration preserves the order of nodes
        // when each descendant node has only one child or is the top leaf
        public IEnumerable<TokenZipNode> GetDescendants(bool andSelf = false)
        {
            if (andSelf) yield return this;
            foreach (TokenZipNode cn in this.ChildNodes)
            {
                foreach (TokenZipNode cnd in cn.GetDescendants(true)) //TODO: recursion seems too slow here.
                {
                    yield return cnd;
                }
            }
        }

        // the sum score of all (leaves weight chain * leaves encounter / maxEncounters)
        public float GetMaxScore(int maxEncounters) // TODO: refactor maxEncounters to be counted within the method because repetitions within the same item's tokens can lead to more encounters than items
        {
            float score = 0.0f;
            foreach (TokenZipNode c in this.ChildNodes)
            {
                score += c.GetMaxScore(this.TokenWeight, maxEncounters, 1);
            }
            return score;
        }

        private float GetMaxScore(float tokenWeightSum, int maxEncounters, int height)
        {
            if (!this.ChildNodes.Any())
                return (tokenWeightSum + this.TokenWeight) * height * this.Encounters / maxEncounters;

            float score = 0.0f;
            foreach (TokenZipNode c in this.ChildNodes)
            {
                score += c.GetMaxScore(tokenWeightSum + this.TokenWeight, maxEncounters, height + 1);
            }
            return score;
        }

        // the sum of all zip chains' last matching nodes' (weight chain * encounter / maxEncounter)
        public float GetScore(IEnumerable<Token> tokenSequence, int maxEncounters)// TODO: refactor maxEncounters to be counted within the method because repetitions within the same item's tokens can lead to more encounters than items
        {
            float score = 0;

            TokenZipNode stepNode = this;
            int height = 0;
            float stepScore = 0;
            foreach (Token token in tokenSequence)
            {
                TokenZipNode matchingChild;

                if (stepNode.ChildTokenIds.TryGetValue(token.Id, out matchingChild) ||
                    stepNode.ChildTokenIds.TryGetValue(WildcardId, out matchingChild))
                {
                    stepNode = matchingChild;
                    stepScore += matchingChild.TokenWeight;
                    height++;
                }
                else if (this.ChildTokenIds.TryGetValue(token.Id, out matchingChild) ||
                  this.ChildTokenIds.TryGetValue(WildcardId, out matchingChild))
                {
                    stepNode = matchingChild;
                    stepScore += matchingChild.TokenWeight;
                    height = 1;
                }
                else
                {
                    if (stepNode != this)
                        score += stepScore * height * stepNode.Encounters / maxEncounters;

                    if (!this.ChildTokenIds.TryGetValue(token.Id, out stepNode))
                    {
                        stepNode = this;
                        stepScore = 0;
                        height = 0;
                    }

                }
            }

            score += stepScore * height * stepNode.Encounters / maxEncounters;

            return score;
        }

        /// <summary>
        /// Adds the sequence to the zip tree, updates Encounters
        /// </summary>
        public void Append(IEnumerable<Token> tokens)
        {
            TokenZipNode stepNode = this;
            foreach (Token token in tokens)
            {
                stepNode.Encounters++;
                TokenZipNode existingNode = null;
                stepNode = stepNode.AppendOrGetExisting(token, out existingNode) ? existingNode : this;
            }
            if (stepNode != this) stepNode.Encounters++;
        }

        // returns true when there is an existing node
        protected bool AppendOrGetExisting(Token token, out TokenZipNode existingNode)
        {
            if (this.ChildTokenIds.TryGetValue(WildcardId, out existingNode))
            {
                // Debug.WriteLine("Found *: " + existingNode.DebuggerDisplay);
                return true;
            }

            if (!this.ChildTokenIds.TryGetValue(token.Id, out existingNode))
            {
                TokenZipNode node = this.CreateChildNode(token.Id, token.Weight);

                this.AppendNewNode(node);
            }
            else
            {
                // Debug.WriteLine("Found: " + existingNode.DebuggerDisplay);
            }

            return existingNode != null;
        }

        protected void RemoveChildNode(int tokenId)
        {
            TokenZipNode childNode;
            if (this.ChildTokenIds.TryGetValue(tokenId, out childNode))
            {
                this.ChildTokenIds.Remove(tokenId);
                this.ChildNodes.Remove(childNode);

                this.DescendantsCount -= childNode.DescendantsCount + 1;
                if (!this.ChildNodes.Any())
                {
                    this.DistanceToTop = 0;
                }
                else if (this.DistanceToTop == childNode.DistanceToTop + 1)
                {
                    this.DistanceToTop = this.ChildNodes.Max(cn => cn.DistanceToTop) + 1;
                }

                this.CheckCompact = true;
            }
        }

        protected void AppendNewNode(TokenZipNode node, bool updateParents = true)
        {
            bool hasChildren = this.ChildNodes.Count > 0;

            this.ChildTokenIds.Add(node.TokenId, node);
            this.ChildNodes.Add(node);
            // Debug.WriteLine("Appended: " + node.DebuggerDisplay + " to ChildNodes: " + ChildNodes.GetHashCode());

            this.DistanceToTop = Math.Min(1, this.DistanceToTop);
            TokenZipNode pNode = this;
            while (pNode != null)
            {
                pNode.DescendantsCount += 1;
                pNode.CheckCompact = true;
                if (!hasChildren) pNode.DistanceToTop += 1;
                if (pNode.IsRoot)
                {
                    pNode.AllTokenIDs.Add(node.TokenId);
                }
                pNode = pNode.Parent;
            }
        }

        public bool Compact()
        {
            if (!this.CheckCompact || this.ChildNodes.Count == 0) return false;
            this.CheckCompact = false; // don't check anymore until a descendant changes

            if (this.ChildNodes.Count == 1)
            {
                TokenZipNode temp = null;
                if (!this.IsRoot && this.DistanceToTop > 1 &&
                    (temp = this.Parent.ChildNodes.FirstOrDefault(cn => cn.ChildTokenIds.ContainsKey(this.TokenId))) != null &&
                    temp.DistanceToTop == this.DistanceToTop && temp.Encounters >= this.Encounters &&
                    temp.GetDescendants().Select(d => d.TokenId).SequenceEqual(this.GetDescendants().Select(d => d.TokenId)))
                {
                    // this node's sequence is already found under its parent node
                    this.Parent.RemoveChildNode(this.TokenId);
                }
                if (!this.IsRoot && this.Parent.ChildNodes.Count >= this.DescendantsCount &&
                    this.DistanceToTop > 1 && this.DescendantsCount == this.DistanceToTop &&
                    this.GetDescendants()
                        .Select((dn, i) => this.Parent.ChildTokenIds.TryGetValue(dn.TokenId, out temp) &&
                                           temp.Encounters + dn.Encounters == this.Encounters)
                        .All((r) => r))
                {
                    // previously unmerged sequence
                    // A2 B1
                    // B1
                    // --> A2 B2
                    IEnumerable<TokenZipNode> descNodes = this.GetDescendants();
                    foreach (TokenZipNode dn in descNodes)
                    {
                        this.Parent.RemoveChildNode(dn.TokenId);
                        dn.Encounters = this.Encounters;
                    }
                    return true;
                }
            }
            else if (this.ChildNodes.Count > 1)
            {
                if (this.DistanceToTop == 1) // the children are leaves
                {
                    // look for common suffix
                    if (this.Parent != null && this.Encounters == this.ChildNodes.Sum(c => c.Encounters) + 1) // +1 because this node was first encountered without any children
                    {
                        if (!this.ChildNodes.Any(c => c.TokenId == this.TokenId))
                        {
                            // this is a leaf-holding branch with variable children we can compact it
                            // but only if the sum of all children's encounters are equal to this node's encounters
                            float freqAvgWeight = this.ChildNodes.Sum(c => c.Encounters * c.TokenWeight) / (this.Encounters - 1);
                            TokenZipNode node = this.CreateChildNode(WildcardId, freqAvgWeight);
                            node.Encounters = this.ChildNodes.Sum(c => c.Encounters);
                            this.CompactChildrenToNode(node);
                            return true;
                        }
                    }
                }
                else if (this.DistanceToTop == 3 && this.ChildNodes.All(c => c.DistanceToTop == 2))
                {
                    // look for common prefix...
                    IEnumerable<TokenZipNode> grandChildren = this.ChildNodes.SelectMany(c => c.ChildNodes);

                    TokenZipNode oneGc = grandChildren.First();
                    if (!this.ChildNodes.Any(c => c.TokenId == oneGc.TokenId))
                    {
                        int sumEncounters = 0;
                        bool allGrandchildrenSame = grandChildren.All(gc =>
                        {
                            sumEncounters += gc.Encounters;
                            return gc.TokenId == oneGc.TokenId;
                        });

                        if (allGrandchildrenSame &&
                            sumEncounters == this.ChildNodes.Sum(c => c.Encounters) &&
                            (this.Parent == null || this.Encounters == sumEncounters))
                        {
                            // all grandchildren are the same, we can compact the children into a wildcard
                            // and the sum encounters of all grandchildren equal that of the child encounters and this node's encounters
                            oneGc.Encounters = sumEncounters;
                            float freqAvgWeight = this.ChildNodes.Average(c => c.Encounters * c.TokenWeight / this.Encounters);
                            TokenZipNode node = this.CreateChildNode(WildcardId, freqAvgWeight);
                            node.Encounters = sumEncounters;
                            TokenZipNode gcNode = node.CreateChildNode(oneGc.TokenId, oneGc.TokenWeight);
                            gcNode.Encounters = node.Encounters - 1;
                            node.AppendNewNode(gcNode);

                            this.CompactChildrenToNode(node); // replace all child nodes with a wildcard node that has the grandchild node in it

                            return true;
                        }
                    }
                }
                else if (!this.IsRoot && this.DistanceToTop > 3 && this.ChildNodes.All(c => c.DistanceToTop > 3 && c.DistanceToTop == c.DescendantsCount))
                {
                    // long prefix
                    IEnumerable<TokenZipNode> steps = this.ChildNodes;
                    int stepTokenId = this.ChildNodes.First().TokenId;
                    if (steps.All(s => s.TokenId == stepTokenId))
                    {
                        // all shoots have the same first token, join them

                    }
                }
            }

            bool oneChildCompacted = false;
            int ci = 0;
            while (ci < this.ChildNodes.Count)
            {
                TokenZipNode ch = this.ChildNodes[ci];
                ci++;
                if (ch.Compact())
                    oneChildCompacted = true;
            }
            return oneChildCompacted;
        }

        private void CompactChildrenToNode(TokenZipNode node)
        {
            this.ChildNodes.Clear();
            this.ChildTokenIds.Clear();
            this.ChildNodes.Add(node);
            this.ChildTokenIds.Add(WildcardId, node);
            TokenZipNode pNode = this;
            while (pNode != null)
            {
                pNode.CheckCompact = true;
                pNode = pNode.Parent;
            }
        }
    }
}
