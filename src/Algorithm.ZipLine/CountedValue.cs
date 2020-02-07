using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Algorithm.ZipLineClustering
{
    /// <summary>
    /// Pair an object with a count, allows the Count to change but not the object
    /// </summary> 
    public class CountedValue<T>
    {
        [JsonProperty("c")]
        public int Count { get; set; }

        [JsonProperty("v", ReferenceLoopHandling = ReferenceLoopHandling.Serialize)]
        public T Value { get; private set; }

        [JsonConstructor]
        protected CountedValue() { }

        public CountedValue(T value)
        {
            this.Value = value;
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is CountedValue<T>)
            {
                return this.Value.Equals((CountedValue<T>)obj);
            }

            return this == obj || this.Value.Equals(obj);
        }

        public static implicit operator T(CountedValue<T> counted)
        {
            return counted.Value;
        }
    }
}
