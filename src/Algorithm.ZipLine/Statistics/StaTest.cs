using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Algorithm.Statistics
{
    public class StaTest
    {
        // The tail values of P
        public enum TPTail // see https://www.medcalc.org/manual/t-distribution.php
        {
            P1000 = 0, // CI 80%, Ptail = 10.00%
            P0500 = 1,
            P0250 = 2,
            P0100 = 3,
            P0050 = 5,
            P0010 = 6,
            P0005 = 7  // CI 99%, Ptail = 0.005%
        }

        // [DF (deg of freedom), A-Value]
        static double[,] TTable = new double[30, 7]  {
            { 3.078, 6.314, 12.706, 31.821, 63.656, 318.289, 636.578}, //DF = 1
            { 1.886, 2.920,  4.303,  6.965,  9.925,  22.328,  31.600}, // DF = 2
            { 1.638, 2.353,  3.182,  4.541,  5.841,  10.214,  12.924}, // ...
            { 1.533,2.132,2.776,3.747,4.604,7.173,8.610},
            { 1.476,2.015,2.571,3.365,4.032,5.894,6.869},
            { 1.440,1.943,2.447,3.143,3.707,5.208,5.959},
            { 1.415,1.895,2.365,2.998,3.499,4.785,5.408},
            { 1.397,1.860,2.306,2.896,3.355,4.501,5.041},
            { 1.383,1.833,2.262,2.821,3.250,4.297,4.781},
            { 1.372,1.812,2.228,2.764,3.169,4.144,4.587},
            { 1.363,1.796,2.201,2.718,3.106,4.025,4.437},
            { 1.356,1.782,2.179,2.681,3.055,3.930,4.318},
            { 1.350,1.771,2.160,2.650,3.012,3.852,4.221},
            { 1.345,1.761,2.145,2.624,2.977,3.787,4.140},
            { 1.341,1.753,2.131,2.602,2.947,3.733,4.073},
            { 1.337,1.746,2.120,2.583,2.921,3.686,4.015},
            { 1.333,1.740,2.110,2.567,2.898,3.646,3.965},
            { 1.330,1.734,2.101,2.552,2.878,3.610,3.922},
            { 1.328,1.729,2.093,2.539,2.861,3.579,3.883},
            { 1.325,1.725,2.086,2.528,2.845,3.552,3.850},
            { 1.323,1.721,2.080,2.518,2.831,3.527,3.819},
            { 1.321,1.717,2.074,2.508,2.819,3.505,3.792},
            { 1.319,1.714,2.069,2.500,2.807,3.485,3.768},
            { 1.318,1.711,2.064,2.492,2.797,3.467,3.745},
            { 1.316,1.708,2.060,2.485,2.787,3.450,3.725},
            { 1.315,1.706,2.056,2.479,2.779,3.435,3.707},
            { 1.314,1.703,2.052,2.473,2.771,3.421,3.689},
            { 1.313,1.701,2.048,2.467,2.763,3.408,3.674},
            { 1.311,1.699,2.045,2.462,2.756,3.396,3.660},
            { 1.310,1.697,2.042,2.457,2.750,3.385,3.646},
        };

        static double[,] ZTable = new double[61, 2]
        {
            {   0   ,   0.5 },
            {   0.05    ,   0.519938806 },
            {   0.1 ,   0.539827837 },
            {   0.15    ,   0.559617692 },
            {   0.2 ,   0.579259709 },
            {   0.25    ,   0.598706326 },
            {   0.3 ,   0.617911422 },
            {   0.35    ,   0.636830651 },
            {   0.4 ,   0.655421742 },
            {   0.45    ,   0.67364478  },
            {   0.5 ,   0.691462461 },
            {   0.55    ,   0.708840313 },
            {   0.6 ,   0.725746882 },
            {   0.65    ,   0.742153889 },
            {   0.7 ,   0.758036348 },
            {   0.75    ,   0.773372648 },
            {   0.8 ,   0.788144601 },
            {   0.85    ,   0.802337457 },
            {   0.9 ,   0.815939875 },
            {   0.95    ,   0.828943874 },
            {   1   ,   0.841344746 },
            {   1.05    ,   0.853140944 },
            {   1.1 ,   0.864333939 },
            {   1.15    ,   0.874928064 },
            {   1.2 ,   0.88493033  },
            {   1.25    ,   0.894350226 },
            {   1.3 ,   0.903199515 },
            {   1.35    ,   0.911492009 },
            {   1.4 ,   0.919243341 },
            {   1.45    ,   0.92647074  },
            {   1.5 ,   0.933192799 },
            {   1.55    ,   0.939429242 },
            {   1.6 ,   0.945200708 },
            {   1.65    ,   0.950528532 },
            {   1.7 ,   0.955434537 },
            {   1.75    ,   0.959940843 },
            {   1.8 ,   0.964069681 },
            {   1.85    ,   0.967843225 },
            {   1.9 ,   0.97128344  },
            {   1.95    ,   0.97441194  },
            {   2   ,   0.977249868 },
            {   2.05    ,   0.979817785 },
            {   2.1 ,   0.982135579 },
            {   2.15    ,   0.984222393 },
            {   2.2 ,   0.986096552 },
            {   2.25    ,   0.987775527 },
            {   2.3 ,   0.98927589  },
            {   2.35    ,   0.990613294 },
            {   2.4 ,   0.991802464 },
            {   2.45    ,   0.992857189 },
            {   2.5 ,   0.993790335 },
            {   2.55    ,   0.994613854 },
            {   2.6 ,   0.995338812 },
            {   2.65    ,   0.995975411 },
            {   2.7 ,   0.996533026 },
            {   2.75    ,   0.997020237 },
            {   2.8 ,   0.99744487  },
            {   2.85    ,   0.997814039 },
            {   2.9 ,   0.998134187 },
            {   2.95    ,   0.99841113  },
            {   3   ,   0.998650102 },
        };

        public static double GetZValue(IEnumerable<double> test, IEnumerable<double> data)
        {
            RunningStat runst = new RunningStat(data);
            double dataM0 = runst.Mean;
            double dataSigma = runst.StandardDeviation;
            int dataCount = runst.Count;
            runst.Push(test);
            double testXBar = runst.Mean;
            dataCount = dataCount - runst.Count; // the count of test items
            double zVal = (testXBar - dataM0) / (dataSigma / Math.Sqrt(dataCount));
            return zVal;
        }

        public static double GetPValue(IEnumerable<double> test, IEnumerable<double> data)
        {
            double zVal = GetZValue(test, data);
            return GetPValueFromZ(zVal);

        }

        public static double GetPValueFromZ(double zValue)
        {
            int zTableIndex = (int)Math.Abs(zValue) * 20; // 20 values per 1Z. the step is 0.05
            if (zTableIndex >= ZTable.GetLength(0) - 1)
            {
                return 0.999;
            }
            else
            {
                return ZTable[zTableIndex, 1];
            }
        }

        // Determines the range of the confidence interval (CI), centered around the mean via T-test
        // Ex: for a given pTail = P0500 for 90% confidence interval
        //     sampleSize = 5
        //     stDev = 1
        // We look up the tValue from the static T-table with DF=sampleSize-1=4 -> 2.132
        // and the returned result is
        // 2.132*1*sqrt(1+1/5) = 2.132*sqrt(0.80) = 2.132*0.894 = 1.906
        //
        // If the mean (xBar) is 10.0 then the 90% CI is [8.093, 11.906]
        public static double GetCiRangeFromS(int sampleSize, double stDev, TPTail pTail = TPTail.P0500)
        {
            // Xbar +- t*S*sqrt(1+1/n)
            int tIndex = Math.Max(0, Math.Min(TTable.GetLength(0) - 1, sampleSize - 1));
            double tVal = TTable[tIndex, (int)pTail];
            double range = tVal * stDev * Math.Sqrt(1 + 1 / (double)sampleSize);
            return range;
        }

        public static TPTail? GetPValueFromT(double testValue, int sampleSize, double mean, double stDev)
        {
            int tIndex = Math.Max(0, Math.Min(TTable.GetLength(0), sampleSize - 1));
            for (int pi = 0; pi < TTable.GetLength(1); pi++)
            {
                double tVal = TTable[tIndex, pi];
                double range = tVal * stDev * Math.Sqrt(1 + 1 / (double)sampleSize);
                if (mean - range >= testValue && testValue >= mean + range) return (TPTail)pi;
            }

            return null;
        }
    }
}
