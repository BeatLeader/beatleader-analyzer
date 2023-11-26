using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal static class Performance
    {
        public static double Average(Span<double> list)
        {
            int offset = 0;
            double sum = 0;
            // First Sum via SIMD Vector Instructions
            while(list.Length - offset >= Vector<double>.Count)
            {
#if NETSTANDARD2_0_OR_GREATER
                sum += Vector.Dot(VectorExtensions.Create<double>(list.Slice(offset, Vector<double>.Count)), Vector<double>.One);
#else
                sum += Vector.Sum(new Vector<double>(list.Slice(offset, Vector<double>.Count)));
#endif
                offset += Vector<double>.Count;
            }
            // If we cant fill another Vector but still have data left, we need to sum it by hand
            if(offset < list.Length)
            {
                foreach (double val in list[offset..])
                {
                    sum += val;
                }
            }
            return sum / list.Length;
        }

        /// <summary>
        /// This type has an internal span, that it fills (and overrides in a circular manner). More info: https://en.wikipedia.org/wiki/Circular_buffer
        /// Also known as ring buffer
        /// </summary>
        /// <param name="buffer"></param>
        public ref struct CircularBuffer(Span<double> buffer)
        {
            public Span<double> Buffer = buffer;
            int nHead = 0;

            public void Enqueue(double val)
            {
                Buffer[nHead++] = val;
                if(nHead == Buffer.Length)
                {
                    nHead = 0;
                }
            }
        }

        #if NETSTANDARD2_0_OR_GREATER
        public static class CollectionsMarshal
        {
            static class ArrayAccessor<T>
            {
                private static readonly FieldInfo fInfo = typeof(List<T>).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic);
                public static T[] GetItems(List<T> list) => (T[])fInfo.GetValue(list);
            }
            public static Span<T> AsSpan<T>(List<T> list) => list is null ? default : new Span<T>(ArrayAccessor<T>.GetItems(list), 0, list.Count);
        }
        #endif
        

        private static class VectorExtensions
        {
#if NETSTANDARD2_0_OR_GREATER
            // Adapted from: source.dot.net/#System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Numerics/Vector_1.cs
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector<T> Create<T>(ReadOnlySpan<T> values) where T : struct
            {
                if(values.Length < Vector<T>.Count)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(nameof(values));
                }

                return Unsafe.ReadUnaligned<Vector<T>>(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(values)));
            }

            private static class ThrowHelper
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static void ThrowArgumentOutOfRangeException(string cMessage) => throw new ArgumentOutOfRangeException(cMessage);
            }
#endif
        }
    }
}
