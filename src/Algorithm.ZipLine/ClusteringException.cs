using System;

namespace Algorithm.ZipLineClustering
{
    public class ClusteringException : Exception
    {
        public enum FaultTypes
        {
            Unknown,
            InconsistentState,
            TokenNotFound,
        }

        public FaultTypes FaultType { get; }

        public ClusteringException(
            FaultTypes faultType,
            string message,
            Exception innerException = null)
            : base(message, innerException)
        {
            this.FaultType = faultType;
        }
    }
}
