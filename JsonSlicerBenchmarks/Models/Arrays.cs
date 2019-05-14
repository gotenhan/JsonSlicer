using System;

namespace JsonSlicerBenchmarks.Models
{
    public class Arrays<T>
    {
        public T[] arr { get; set; }

        public T this[int i]
        {
            get => arr[i];
            set => arr[i] = value;
        }

        public static Arrays<T> Random(Func<Random, T> factory, int size = 100, int? seed = null)
        {
            var rand = seed.HasValue ? new Random(seed.Value) : new Random();
            var arr = new Arrays<T>();
            arr.arr = new T[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = factory(rand);
                //arr.Long1[i] = rand.Next();
                //arr.String1[i] = new string((char)rand.Next('A', 'z'), rand.Next(5, 20));
            }

            return arr;
        }

        public override string ToString()
        {
            return $"{typeof(T).Name}[{arr.Length}";
        }
    }
}