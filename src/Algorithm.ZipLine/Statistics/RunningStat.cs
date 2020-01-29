using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Algorithm.Statistics
{
    /// <summary>
    /// Maintains running-stats data for a numerical series
    /// </summary>
    public class RunningStat
    {
        private int m_count;
        private double m_oldMean, m_newMean;
        private double m_oldS, m_newS;

        [DataMember]
        public double Mo
        {
            get { return this.m_oldMean; }
            private set { this.m_oldMean = value; }
        }

        [DataMember]
        public double So
        {
            get { return this.m_oldS; }
            private set { this.m_oldS = value; }
        }

        [DataMember]
        public double Sn
        {
            get { return this.m_newS; }
            private set { this.m_newS = value; }
        }

        [DataMember]
        public int Count
        {
            get { return this.m_count; }
            private set { this.m_count = value; }
        }

        [DataMember]
        public double Mean
        {
            get { return (this.m_count > 0) ? this.m_newMean : 0.0; ; }
            private set { this.m_newMean = value; }
        }

        public double Variance => ((this.m_count > 1) ? this.m_newS / (this.m_count - 1) : 0.0);

        public double StandardDeviation => this.Count > 1 ? Math.Sqrt(this.Variance) : this.Mean / 2;

        public RunningStat()
        {
        }

        public RunningStat(IEnumerable<double> data) : this()
        {
            this.Push(data);
        }

        /// <summary>
        /// Add a set of new values to the series, updates the statistics
        /// </summary>
        public void Push(IEnumerable<double> addData)
        {
            foreach (double d in addData)
            {
                this.Push(d);
            }
        }

        /// <summary>
        /// Adds a new value to the series, updates the statistics
        /// </summary>
        public void Push(double x)
        {
            this.m_count++;

            if (this.m_count == 1)
            {
                this.m_oldMean = this.m_newMean = x;
                this.m_oldS = 0.0;
            }
            else
            {
                this.m_newMean = this.m_oldMean + (x - this.m_oldMean) / this.m_count;
                this.m_newS = this.m_oldS + (x - this.m_oldMean) * (x - this.m_newMean);

                // set up for next iteration
                this.m_oldMean = this.m_newMean;
                this.m_oldS = this.m_newS;
            }
        }
    }
}
