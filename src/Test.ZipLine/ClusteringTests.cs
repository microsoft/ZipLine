using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Algorithm.ZipLineClustering;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLipsum.Core;

namespace Test.ZipLine
{
    [TestClass]
    public class ClusteringTests
    {
        [TestMethod]
        public void SampleClustersTest()
        {
            const int inputsCount = 1000;
            Clustering clustering = CreateClusteringEngine();

            foreach (string inputText in Enumerable.Range(0, inputsCount).Select(i => "This is sample input with some text added to fluff it up" + i))
            {
                clustering.AddItem(Guid.NewGuid().ToString(), inputText);
            }

            // resultingClusters is a list of clusters, where each cluster is a list of input IDs
            List<List<string>> resultingClusters = clustering.GetClustersItemIds();

            Assert.IsNotNull(resultingClusters);
            Assert.IsTrue(resultingClusters.Count > 0);
            Assert.AreEqual(inputsCount, resultingClusters.Sum(c => c.Count));
        }


        [TestMethod]
        public void LipsumClustersTest()
        {
            const int inputsCount = 1000;
            const int intendedClustersCount = 5;
            var rnd = new Random();

            Clustering clustering = CreateClusteringEngine();

            string rawText = Lipsums.LoremIpsum;
            var lipsum = new LipsumGenerator(rawText, false);

            string[] generatedSentences = lipsum.GenerateSentences(intendedClustersCount * 5, Sentence.Long);
            generatedSentences = generatedSentences.OrderByDescending(s => s.Length).Take(intendedClustersCount).ToArray();

            for (int i = 0; i < inputsCount; i++)
            {
                int intendedCluster = i % generatedSentences.Length;
                string sentence = generatedSentences[intendedCluster];
                // swap two words
                string[] words = sentence.Split(' ');
                int ix1 = rnd.Next(words.Length);
                int ix2 = rnd.Next(words.Length);
                string w1 = words[ix1];
                words[ix1] = words[ix2];
                words[ix2] = w1;

                sentence = string.Join(" ", words);

                clustering.AddItem($"{intendedCluster}-{i}:{ix1}:{ix2}", sentence);
            }

            List<List<string>> resultingClusters = clustering.GetClustersItemIds();

            Assert.IsNotNull(resultingClusters);
            Assert.IsTrue(resultingClusters.Count >= intendedClustersCount);
            foreach (List<string> cluster in resultingClusters)
            {
                IEnumerable<IGrouping<string, string>> desiredGroups = cluster.GroupBy(id => id.Split('-').First());
                Assert.AreEqual(1, desiredGroups.Count());
            }
        }

        private static Clustering CreateClusteringEngine()
        {
            var config = new ClusteringConfig
            {
                MinClusterAffinity = 0.85f,
                WeightedTokens = new Dictionary<string, float>
                {
                    {"Important Fragment Regex Here", 10}
                }
            };

            return new Clustering(config);
        }
    }
}
