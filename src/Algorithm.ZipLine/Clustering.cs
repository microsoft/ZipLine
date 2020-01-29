using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Algorithm.ZipLineClustering.ClusterTypes;
using Newtonsoft.Json;

namespace Algorithm.ZipLineClustering
{
    /// <summary>
    /// This class represents the clustering engine.
    /// It uses ClusteringConfig to determine parameters for the actual algorithm, keeps a list of clusters and token vocabulary
    /// This class can be serialized/deserialized as JSON to persist its state.
    /// </summary>
    public class Clustering
    {
        /// <summary>
        /// Hash function, used to recognize identical items
        /// </summary>
        public static Func<string, string> PlatformGetStringHash = (data) =>
        {
            if (string.IsNullOrEmpty(data)) return "0|K64";

            UInt64 hashedValue = 3074457345618258791ul;
            for (int i = 0; i < data.Length; i++)
            {
                hashedValue += data[i];
                hashedValue *= 3074457345618258799ul;
            }

            return data.Substring(0, Math.Min(3, data.Length)) + hashedValue + data.Length + "|K64";
        };

        // when deserialized from JSON this flag is set to true and causes init method to do work
        private bool m_pendingInit = false;

        [JsonProperty]
        public ClusteringConfig Config { get; private set; }

        private List<ClusterBase> m_clusters = new List<ClusterBase>();
        [JsonProperty(TypeNameHandling = TypeNameHandling.Arrays)]
        public List<ClusterBase> Clusters
        {
            get { return this.m_clusters; }

            private set
            {
                this.m_clusters = value;
                this.InitIfPending();
            }
        }

        public int ClusterCount => this.Clusters.Count;

        private ClusteringVocabulary m_vocabulary;
        [JsonProperty]
        public ClusteringVocabulary Vocabulary
        {
            get { return this.m_vocabulary; }
            set
            {
                this.m_vocabulary = value;
                this.InitIfPending();
            }
        }

        [JsonConstructor]
        protected Clustering()
        {
            this.m_pendingInit = true;
        }

        public Clustering(ClusteringConfig config)
        {
            this.Config = config;
            this.m_vocabulary = new ClusteringVocabulary(config);
        }

        /// <summary>
        /// Check if this item id is present in any clusters
        /// </summary>
        public bool IsKnownItem(string id)
        {
            return this.Clusters.Any(c => c.Items.ContainsKey(id));
        }

        /// <summary>
        /// If the clustering data was loaded by deserializing it, it may have not been yet initialized.
        /// This method ensures the internal state of the clustering engine is initialized.
        /// </summary>
        public virtual bool InitIfPending()
        {
            if (this.m_pendingInit)
            {
                this.m_pendingInit = false;
                if (this.Vocabulary != null && this.Clusters != null)
                {
                    this.Vocabulary.Config = this.Config;
                    foreach (ClusterBase cluster in this.Clusters)
                    {
                        cluster.Config = this.Config;
                        cluster.InitIfPending(this, this.Vocabulary);
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes an item from a cluster but may not affect the cluster's internal state
        /// </summary>
        /// <returns>true if found and removed</returns>
        public bool RemoveItem(string id, ClusterBase clusterOwner = null)
        {
            clusterOwner = clusterOwner ?? this.Clusters.FirstOrDefault(c => c.Items.ContainsKey(id));
            if (clusterOwner != null)
            {
                clusterOwner.Items.Remove(id);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds a new clustering item
        /// </summary>
        /// <param name="id">ID to track this item</param>
        /// <param name="content">The item's contents - this is what the clustering algorithm parses</param>
        public void AddItem(string id, string content)
        {
            if (!this.IsKnownItem(id))
            {
                this.InitIfPending();

                var item = new ClusterItem(id, content, this.Vocabulary);
                List<ClusterBase> checkClusters = null;

                float bestAffinity = 0.0f;
                for (int c = 0; c < this.Clusters.Count; c++)
                {
                    if ((this.Clusters[c].LastItemHash != null && this.Clusters[c].LastItemHash == item.Hash)
                        || (this.Clusters[c].StatsAffinity.Mean - this.Clusters[c].StatsAffinity.StandardDeviation > this.Config.MinClusterAffinity
                            && !string.IsNullOrEmpty(item.Hash) && this.Clusters[c].ItemContentHashes.Contains(item.Hash)))
                    {
                        checkClusters = checkClusters ?? new List<ClusterBase>();
                        checkClusters.Add(this.Clusters[c]);
                        bestAffinity = 1.0f;
                    }
                }

                int bestCluster = -1;
                checkClusters = checkClusters ?? this.Clusters;

                if (checkClusters.Count <= 1)
                {
                    if (checkClusters.Count == 1)
                    {
                        bestCluster = 0;
                        if (bestAffinity < 0.99999f)
                        {
                            bestAffinity = checkClusters[0].GetAffinity(item, this.Config.MinClusterAffinity);
                        }
                    }
                }
                else
                {
                    bool breakOnMax = checkClusters.Count > 5;

                    if (this.Config.LogDebug && checkClusters.Count < this.Clusters.Count)
                    {
                        Debug.WriteLine($"Clusters ({checkClusters.Count} of {this.Clusters.Count}) found by hash. Item {item.Id}, Hash: {item.Hash} Content:{content.Replace('\r', ' ').Replace('\n', ' ')}");
                    }

                    float[] clusterAffinity = new float[checkClusters.Count];

                    Parallel.For(0, checkClusters.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, this.Config.MaxDegreeOfParallelism) },
                        (ci, state) =>
                        {
                            ClusterBase cluster = checkClusters[ci];

                            float caff = cluster.GetAffinity(item, this.Config.MinClusterAffinity);
                            clusterAffinity[ci] = caff;
                            if (breakOnMax && caff > 0.99999)
                            {
                                bestCluster = ci;
                                bestAffinity = caff;
                                state.Stop();
                            }
                        });

                    if (bestCluster < 0)
                    {
                        bestAffinity = 0;
                        for (int ci = 0; ci < checkClusters.Count; ci++)
                        {
                            if (clusterAffinity[ci] > bestAffinity)
                            {
                                bestAffinity = clusterAffinity[ci];
                                bestCluster = ci;
                            }
                        }
                    }
                }

                if (bestCluster >= 0 && bestAffinity < this.Config.MinClusterAffinity)
                {
                    bestCluster = -1;
                }
                if (bestCluster >= 0)
                {
                    ClusterBase cluster = checkClusters[bestCluster];
                    cluster.AddToCluster(item, cluster.GetAffinity(item, this.Config.MinClusterAffinity));
                }
                else
                {
                    var newCluster = this.CreateCluster(item);
                    this.Clusters.Add(newCluster);
                }
            }
        }

        /// <summary>
        /// Returns a list of item ids in each cluster
        /// </summary>
        public List<List<string>> GetClustersItemIds()
        {
            this.InitIfPending();
            return this.Clusters
                .Select(sec => sec.Items.Keys.ToList())
                .ToList();
        }

        /// <summary>
        /// Creates a new cluster object for an item.
        /// Override this method to replace the clustering algorithm
        /// </summary>
        protected virtual ClusterBase CreateCluster(ClusterItem item)
        {
            ClusterBase cluster = new ZipLineCluster(this.Config, this.Vocabulary);
            cluster.AddToCluster(item, 1.0f);
            return cluster;
        }
    }
}
