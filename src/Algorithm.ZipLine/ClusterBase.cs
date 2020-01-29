using System;
using System.Collections.Generic;
using System.Linq;
using Algorithm.ZipLineClustering.ClusterTypes;
using Newtonsoft.Json;
using Algorithm.Statistics;
using static Algorithm.Statistics.StaTest;
using System.Diagnostics;

namespace Algorithm.ZipLineClustering
{
    /// <summary>
    /// Provides a base class for all clustering algorithms
    /// </summary>
    public abstract class ClusterBase
    {
        [JsonProperty]
        public Guid ClId { get; protected set; } = Guid.NewGuid();

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Guid? SplitFromClusterId { get; protected set; }

        [JsonIgnore]
        public ClusteringConfig Config { get; set; }

        [JsonProperty]
        public RunningStat StatsTextLength { get; private set; } = new RunningStat();

        [JsonProperty]
        public RunningStat StatsTokenCount { get; private set; } = new RunningStat();

        [JsonProperty]
        public RunningStat StatsAffinity { get; private set; } = new RunningStat();

        [JsonProperty("Items", Order = 99)]
        public Dictionary<string, ClusterItem> Items { get; private set; } = new Dictionary<string, ClusterItem>();

        [JsonIgnore]
        public HashSet<string> ItemContentHashes { get; private set; } = new HashSet<string>();

        [JsonIgnore]
        protected List<ClusterTokenIndex> VerificationTokenIndexes = null;

        public string LastItemHash { get; protected set; }

        private WeakReference<ClusterItem> m_lastItem;
        [JsonIgnore]
        public ClusterItem LastItem
        {
            get
            {
                ClusterItem item = null;
                return this.m_lastItem?.TryGetTarget(out item) == true ? item : null;
            }
            set
            {
                if (value != null)
                {
                    this.LastItemHash = value.Hash ?? this.LastItemHash;

                    if (this.m_lastItem != null)
                        this.m_lastItem.SetTarget(value);
                    else
                        this.m_lastItem = new WeakReference<ClusterItem>(value);
                }
                else
                    this.m_lastItem = null;
            }
        }

        public static int TokenParts = 3;
        private bool m_initPending = false;

        public static bool IsSmallItem(ClusterItem item) => item.TokenIndex.TotalCount < 7 * TokenParts;

        [JsonConstructor]
        protected ClusterBase()
        {
            this.m_initPending = true;
        }

        protected ClusterBase(ClusteringConfig config)
        {
            this.Config = config;
        }

        public float GetAffinity(ClusterItem item, float minAffinity)
        {
            // trivial case - already in cluster
            if (this.Items.ContainsKey(item.Id)) return 1.0f;
            // trivial case - new cluster
            if (!this.Items.Any())
            {
                return 1.0f;
            }

            if ((item.TextLength == 0 || item.Content == null) && this.StatsTextLength.Mean < 1)
            {
                return 1.0f;
            }



            if (this.Config.StDevFactorTextLength > 0)
            {
                if (this.CalculateConfidence(this.StatsTextLength, item.TextLength, this.Config.StDevFactorTextLength, 5) == null)
                {
                    if (this.Config.LogDebug)
                    {
                        Debug.WriteLine($"[TSCBSGIETUJN] Item {item.Id} excluded for text length");
                    }

                    return 0;
                }
            }

            if (this.Config.StDevFactorTokenCount > 0)
            {
                if (this.CalculateConfidence(this.StatsTokenCount, item.TokenIndex.TotalCount, this.Config.StDevFactorTokenCount, 2) == null)
                {
                    if (this.Config.LogDebug)
                    {
                        Debug.WriteLine($"Item {item.Id} excluded for token count");
                    }

                    return 0;
                }
            }

            float affinity = this.OnGetAffinity(item, minAffinity);

            if (affinity > this.Config.MinClusterAffinity && this.Config.StDevFactorAffinity > 0 && this.Items.Count >= this.Config.MinClusterSizeToSplit)
            {
                float reAff = (float)(this.CalculateConfidence(this.StatsAffinity, affinity, this.Config.StDevFactorAffinity, 0.015, false) ?? 0.0);
                if (this.Config.LogDebug && reAff < this.Config.MinClusterAffinity)
                {
                    Debug.WriteLine($"Item {item.Id} excluded for affinity");
                }

                return reAff;
            }

            return affinity;
        }

        /// <summary>
        /// Returns null if the test value is below the lower bound of 90% confidence
        /// Otherwise returns an ammended testValue, such as testValue*pValue
        /// </summary>
        protected virtual double? CalculateConfidence(RunningStat statistic, double testValue, double stDevFactor, double stdevMinValue, bool isMacro = true)
        {
            const StaTest.TPTail pTail = TPTail.P0500; /* 95% CI */

            // ciRange is calculated using T value to account for
            double ciRange = StaTest.GetCiRangeFromS(Math.Max(0, (int)Math.Ceiling(Math.Sqrt(statistic.Count))), Math.Max(stdevMinValue, statistic.StandardDeviation) * stDevFactor, pTail);
            if (isMacro)
            {
                if (ciRange > statistic.Mean)
                {
                    // this happens when there are too few items in a cluster and the T value is too high
                    // in that case the constraint is harder on the expected text length or token count
                    ciRange = Math.Min(ciRange, statistic.Mean * (1 - this.Config.MinClusterAffinity) * stDevFactor);
                }
                else
                {
                    // macro statistics shouldn't get too tight, so the minimum range is not allowed to shrink even if statistics tell it to.
                    // the actual minimum range considered depends on the min affinity
                    // Example: for min affinity 0.8, if the cluster has a mean of 100 tokens and StDev is 2, then ciRange might be 4,
                    //          but the forula below will make it 100*0.2*0.8 = 16
                    ciRange = Math.Max(ciRange, statistic.Mean * (1 - this.Config.MinClusterAffinity) * this.Config.MinClusterAffinity);
                }
            }

            // if the test statistic is outside the acceptable range [Mean - ciRange, Mean + ciRange] then this is an outlier and will be rejected
            if (testValue < statistic.Mean - ciRange || testValue > statistic.Mean + ciRange)
                return null;
            if (testValue > statistic.Mean)
                return testValue;

            double zTest = Math.Abs(testValue - statistic.Mean) / (statistic.StandardDeviation * 2);
            double pVal = StaTest.GetPValueFromZ(zTest);
            double probWt = 2 * (1 - pVal);
            float result = (float)(testValue * probWt);
            return result;
        }

        protected abstract float OnGetAffinity(ClusterItem item, float minAffinity);

        internal void AddToCluster(ClusterItem item, float affinity)
        {
            if (!this.Items.ContainsKey(item.Id))
            {
                this.StatsTextLength.Push(item.TextLength);
                this.StatsTokenCount.Push(item.TokenIndex.TotalCount);
                this.StatsAffinity.Push(affinity);

                this.Items.Add(item.Id, item);

                this.LastItem = item;

                if (!string.IsNullOrEmpty(item.Hash))
                {
                    // if the bucket is already "sharp" enough then preserve the
                    if (this.StatsAffinity.Mean - this.StatsAffinity.StandardDeviation > this.Config.MinClusterAffinity)
                    {
                        if (this.ItemContentHashes.Count == 0)
                        {
                            foreach (ClusterItem itm in this.Items.Values.Where(it => !string.IsNullOrEmpty(it.Hash)))
                            {
                                if (this.ItemContentHashes.Count < 100)
                                    this.ItemContentHashes.Add(itm.Hash);
                                else
                                    break;
                            }
                        }

                        if (this.ItemContentHashes.Contains(item.Hash))
                        {
                            item.PreserveHash = false;
                        }
                        else if (this.ItemContentHashes.Count < 100) // 100 hashes at most. The buckets where it helps most have very few hashes
                        {
                            this.ItemContentHashes.Add(item.Hash);
                        }
                    }
                }

                item.Affinity = affinity;
                this.OnAddToCluster(item, affinity);
                item.OnAdded();
            }
        }

        protected virtual void OnAddToCluster(ClusterItem item, float affinity)
        {
        }

        public virtual bool InitIfPending(Clustering clustering, ClusteringVocabulary vocabulary)
        {
            if (this.m_initPending)
            {
                this.m_initPending = false;

                foreach (ClusterItem item in this.Items.Values)
                {
                    item.InitIfPending(vocabulary);
                    if (!string.IsNullOrEmpty(item.Hash))
                        this.ItemContentHashes.Add(item.Hash);
                }

                return true;
            }

            return false;
        }
    }
}