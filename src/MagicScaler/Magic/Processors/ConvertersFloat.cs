// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
#if HWINTRINSICS
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

using static PhotoSauce.MagicScaler.MathUtil;

using VectorF = System.Numerics.Vector<float>;

namespace PhotoSauce.MagicScaler.Converters;

internal static class FloatConverter
{
	public sealed class Widening : IConverter<byte, float>
	{
		public static readonly Widening InstanceFullRange = new();
		public static readonly Widening InstanceFullChroma = new(128, 255);
		public static readonly Widening InstanceVideoRange = new(16, 219);
		public static readonly Widening InstanceVideoChroma = new(128, 224);

		private static readonly WideningImpl3A processor3A = new();
		private static readonly WideningImpl3X processor3X = new();

		private readonly WideningImpl processor;

		private Widening(int offset = 0, int scale = byte.MaxValue) => processor = new WideningImpl(offset, scale);

		public IConversionProcessor<byte, float> Processor => processor;
		public IConversionProcessor<byte, float> Processor3A => processor3A;
		public IConversionProcessor<byte, float> Processor3X => processor3X;
	}

	public sealed class Narrowing : IConverter<float, byte>
	{
		public static readonly Narrowing InstanceFullRange = new();
		public static readonly Narrowing InstanceFullChroma = new(128, 255);
		public static readonly Narrowing InstanceVideoRange = new(16, 219);
		public static readonly Narrowing InstanceVideoChroma = new(128, 224);

		private static readonly NarrowingImpl3A processor3A = new();
		private static readonly NarrowingImpl3X processor3X = new();

		private readonly NarrowingImpl processor;

		private Narrowing(int offset = 0, int scale = byte.MaxValue) => processor = new NarrowingImpl(offset, scale);

		public IConversionProcessor<float, byte> Processor => processor;
		public IConversionProcessor<float, byte> Processor3A => processor3A;
		public IConversionProcessor<float, byte> Processor3X => processor3X;
	}

	private sealed unsafe class WideningImpl(int offset, int scale) : IConversionProcessor<byte, float>
	{
		private readonly float scale = 1f / scale;
		private readonly float offset = (float)-offset / scale;
		private readonly float[] valueTable = offset != 0 || scale != byte.MaxValue ? makeTable(offset, scale) : LookupTables.Alpha;

		private static float[] makeTable(int offset, int scale)
		{
			var tbl = new float[256];
			for (int i = 0; i < tbl.Length; i++)
				tbl[i] = (float)((double)(i - offset) / scale);

			return tbl;
		}

		void IConversionProcessor.ConvertLine(byte* istart, byte* ostart, nint cb)
		{
			byte* ip = istart, ipe = istart + cb;
			float* op = (float*)ostart;

#if HWINTRINSICS
			if (HWIntrinsics.IsSupported && cb >= HWIntrinsics.VectorCount<byte>())
				convertIntrinsic(ip, ipe, op);
			else
#endif
#if VECTOR_CONVERT
			if (!HWIntrinsics.IsSupported && cb >= Vector<byte>.Count)
				convertVector(ip, ipe, op);
			else
#endif
				convertScalar(ip, ipe, op);
		}

#if HWINTRINSICS
		private void convertIntrinsic(byte* ip, byte* ipe, float* op)
		{
			if (Avx2.IsSupported)
			{
				var vscal = Vector256.Create(scale);
				var voffs = Vector256.Create(offset);
				ipe -= Vector256<byte>.Count;

				var vlast = Avx.LoadVector256(ipe);

				LoopTop:
				do
				{
					var vi0 = Avx2.ConvertToVector256Int32(ip);
					var vi1 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count);
					var vi2 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 2);
					var vi3 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 3);
					ip += Vector256<byte>.Count;

					var vf0 = Avx.ConvertToVector256Single(vi0);
					var vf1 = Avx.ConvertToVector256Single(vi1);
					var vf2 = Avx.ConvertToVector256Single(vi2);
					var vf3 = Avx.ConvertToVector256Single(vi3);

					vf0 = HWIntrinsics.MultiplyAdd(voffs, vf0, vscal);
					vf1 = HWIntrinsics.MultiplyAdd(voffs, vf1, vscal);
					vf2 = HWIntrinsics.MultiplyAdd(voffs, vf2, vscal);
					vf3 = HWIntrinsics.MultiplyAdd(voffs, vf3, vscal);

					Avx.Store(op, vf0);
					Avx.Store(op + Vector256<float>.Count, vf1);
					Avx.Store(op + Vector256<float>.Count * 2, vf2);
					Avx.Store(op + Vector256<float>.Count * 3, vf3);
					op += Vector256<float>.Count * 4;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector256<byte>.Count)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<byte, float>(offs));
					Avx.Store(ip, vlast);
					goto LoopTop;
				}
			}
			else
			{
				var vscal = Vector128.Create(scale);
				var voffs = Vector128.Create(offset);
				var vzero = Vector128<byte>.Zero;
				ipe -= Vector128<byte>.Count;

				var vlast = Sse2.LoadVector128(ipe);

				LoopTop:
				do
				{
					Vector128<int> vi0, vi1, vi2, vi3;
					if (Sse41.IsSupported)
					{
						vi0 = Sse41.ConvertToVector128Int32(ip);
						vi1 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count);
						vi2 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 2);
						vi3 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 3);
					}
					else
					{
						var vb = Sse2.LoadVector128(ip);
						var vs0 = Sse2.UnpackLow(vb, vzero).AsInt16();
						var vs1 = Sse2.UnpackHigh(vb, vzero).AsInt16();
						vi0 = Sse2.UnpackLow(vs0, vzero.AsInt16()).AsInt32();
						vi1 = Sse2.UnpackHigh(vs0, vzero.AsInt16()).AsInt32();
						vi2 = Sse2.UnpackLow(vs1, vzero.AsInt16()).AsInt32();
						vi3 = Sse2.UnpackHigh(vs1, vzero.AsInt16()).AsInt32();
					}
					ip += Vector128<byte>.Count;

					var vf0 = Sse2.ConvertToVector128Single(vi0);
					var vf1 = Sse2.ConvertToVector128Single(vi1);
					var vf2 = Sse2.ConvertToVector128Single(vi2);
					var vf3 = Sse2.ConvertToVector128Single(vi3);

					vf0 = HWIntrinsics.MultiplyAdd(voffs, vf0, vscal);
					vf1 = HWIntrinsics.MultiplyAdd(voffs, vf1, vscal);
					vf2 = HWIntrinsics.MultiplyAdd(voffs, vf2, vscal);
					vf3 = HWIntrinsics.MultiplyAdd(voffs, vf3, vscal);

					Sse.Store(op, vf0);
					Sse.Store(op + Vector128<float>.Count, vf1);
					Sse.Store(op + Vector128<float>.Count * 2, vf2);
					Sse.Store(op + Vector128<float>.Count * 3, vf3);
					op += Vector128<float>.Count * 4;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector128<byte>.Count)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<byte, float>(offs));
					Sse2.Store(ip, vlast);
					goto LoopTop;
				}
			}
		}
#endif

#if VECTOR_CONVERT
		private void convertVector(byte* ip, byte* ipe, float* op)
		{
			var vscal = new VectorF(scale);
			var voffs = new VectorF(offset);
			ipe -= Vector<byte>.Count;

			var vlast = Unsafe.ReadUnaligned<Vector<byte>>(ipe);

			LoopTop:
			do
			{
				var vb = Unsafe.ReadUnaligned<Vector<byte>>(ip);
				Vector.Widen(vb, out var vs0, out var vs1);
				Vector.Widen(vs0, out var vi0, out var vi1);
				Vector.Widen(vs1, out var vi2, out var vi3);
				ip += Vector<byte>.Count;

				var vf0 = Vector.ConvertToSingle(Vector.AsVectorInt32(vi0));
				var vf1 = Vector.ConvertToSingle(Vector.AsVectorInt32(vi1));
				var vf2 = Vector.ConvertToSingle(Vector.AsVectorInt32(vi2));
				var vf3 = Vector.ConvertToSingle(Vector.AsVectorInt32(vi3));

				vf0 = vf0 * vscal + voffs;
				vf1 = vf1 * vscal + voffs;
				vf2 = vf2 * vscal + voffs;
				vf3 = vf3 * vscal + voffs;

				Unsafe.WriteUnaligned(op, vf0);
				Unsafe.WriteUnaligned(op + VectorF.Count, vf1);
				Unsafe.WriteUnaligned(op + VectorF.Count * 2, vf2);
				Unsafe.WriteUnaligned(op + VectorF.Count * 3, vf3);
				op += VectorF.Count * 4;
			}
			while (ip <= ipe);

			if (ip < ipe + Vector<byte>.Count)
			{
				nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
				ip = UnsafeUtil.SubtractOffset(ip, offs);
				op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<byte, float>(offs));
				Unsafe.WriteUnaligned(ip, vlast);
				goto LoopTop;
			}
		}
#endif

		private void convertScalar(byte* ip, byte* ipe, float* op)
		{
			fixed (float* atstart = &valueTable.GetDataRef())
			{
				float* at = atstart;

				ipe -= 8;
				while (ip <= ipe)
				{
					float o0 = at[(nuint)ip[0]];
					float o1 = at[(nuint)ip[1]];
					float o2 = at[(nuint)ip[2]];
					float o3 = at[(nuint)ip[3]];
					float o4 = at[(nuint)ip[4]];
					float o5 = at[(nuint)ip[5]];
					float o6 = at[(nuint)ip[6]];
					float o7 = at[(nuint)ip[7]];
					ip += 8;

					op[0] = o0;
					op[1] = o1;
					op[2] = o2;
					op[3] = o3;
					op[4] = o4;
					op[5] = o5;
					op[6] = o6;
					op[7] = o7;
					op += 8;
				}
				ipe += 8;

				while (ip < ipe)
				{
					op[0] = at[(nuint)ip[0]];
					ip++;
					op++;
				}
			}
		}
	}

	private sealed unsafe class WideningImpl3A : IConversionProcessor<byte, float>
	{
		void IConversionProcessor.ConvertLine(byte* istart, byte* ostart, nint cb)
		{
			byte* ip = istart, ipe = istart + cb;
			float* op = (float*)ostart;

#if HWINTRINSICS
			if (Sse41.IsSupported && cb >= HWIntrinsics.VectorCount<byte>())
				convertIntrinsic(ip, ipe, op);
			else
#endif
				convertScalar(ip, ipe, op);
		}

#if HWINTRINSICS
		private static void convertIntrinsic(byte* ip, byte* ipe, float* op)
		{
			if (Avx2.IsSupported)
			{
				var vscale = Vector256.Create(1f / byte.MaxValue);
				ipe -= Vector256<byte>.Count;

				var vlast = Avx.LoadVector256(ipe);

				LoopTop:
				do
				{
					var vi0 = Avx2.ConvertToVector256Int32(ip);
					var vi1 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count);
					var vi2 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 2);
					var vi3 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 3);
					ip += Vector256<byte>.Count;

					var vf0 = Avx.ConvertToVector256Single(vi0);
					var vf1 = Avx.ConvertToVector256Single(vi1);
					var vf2 = Avx.ConvertToVector256Single(vi2);
					var vf3 = Avx.ConvertToVector256Single(vi3);

					vf0 = Avx.Multiply(vf0, vscale);
					vf1 = Avx.Multiply(vf1, vscale);
					vf2 = Avx.Multiply(vf2, vscale);
					vf3 = Avx.Multiply(vf3, vscale);

					var vfa0 = Avx.Shuffle(vf0, vf0, HWIntrinsics.ShuffleMaskAlpha);
					var vfa1 = Avx.Shuffle(vf1, vf1, HWIntrinsics.ShuffleMaskAlpha);
					var vfa2 = Avx.Shuffle(vf2, vf2, HWIntrinsics.ShuffleMaskAlpha);
					var vfa3 = Avx.Shuffle(vf3, vf3, HWIntrinsics.ShuffleMaskAlpha);

					vf0 = Avx.Multiply(vf0, vfa0);
					vf1 = Avx.Multiply(vf1, vfa1);
					vf2 = Avx.Multiply(vf2, vfa2);
					vf3 = Avx.Multiply(vf3, vfa3);

					vf0 = Avx.Blend(vf0, vfa0, HWIntrinsics.BlendMaskAlpha);
					vf1 = Avx.Blend(vf1, vfa1, HWIntrinsics.BlendMaskAlpha);
					vf2 = Avx.Blend(vf2, vfa2, HWIntrinsics.BlendMaskAlpha);
					vf3 = Avx.Blend(vf3, vfa3, HWIntrinsics.BlendMaskAlpha);

					Avx.Store(op, vf0);
					Avx.Store(op + Vector256<float>.Count, vf1);
					Avx.Store(op + Vector256<float>.Count * 2, vf2);
					Avx.Store(op + Vector256<float>.Count * 3, vf3);
					op += Vector256<float>.Count * 4;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector256<byte>.Count)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<byte, float>(offs));
					Avx.Store(ip, vlast);
					goto LoopTop;
				}
			}
			else // Sse4.1
			{
				var vscale = Vector128.Create(1f / byte.MaxValue);
				ipe -= Vector128<byte>.Count;

				var vlast = Sse2.LoadVector128(ipe);

				LoopTop:
				do
				{
					var vi0 = Sse41.ConvertToVector128Int32(ip);
					var vi1 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count);
					var vi2 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 2);
					var vi3 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 3);
					ip += Vector128<byte>.Count;

					var vf0 = Sse2.ConvertToVector128Single(vi0);
					var vf1 = Sse2.ConvertToVector128Single(vi1);
					var vf2 = Sse2.ConvertToVector128Single(vi2);
					var vf3 = Sse2.ConvertToVector128Single(vi3);

					vf0 = Sse.Multiply(vf0, vscale);
					vf1 = Sse.Multiply(vf1, vscale);
					vf2 = Sse.Multiply(vf2, vscale);
					vf3 = Sse.Multiply(vf3, vscale);

					var vfa0 = Sse.Shuffle(vf0, vf0, HWIntrinsics.ShuffleMaskAlpha);
					var vfa1 = Sse.Shuffle(vf1, vf1, HWIntrinsics.ShuffleMaskAlpha);
					var vfa2 = Sse.Shuffle(vf2, vf2, HWIntrinsics.ShuffleMaskAlpha);
					var vfa3 = Sse.Shuffle(vf3, vf3, HWIntrinsics.ShuffleMaskAlpha);

					vf0 = Sse.Multiply(vf0, vfa0);
					vf1 = Sse.Multiply(vf1, vfa1);
					vf2 = Sse.Multiply(vf2, vfa2);
					vf3 = Sse.Multiply(vf3, vfa3);

					vf0 = Sse41.Blend(vf0, vfa0, HWIntrinsics.BlendMaskAlpha);
					vf1 = Sse41.Blend(vf1, vfa1, HWIntrinsics.BlendMaskAlpha);
					vf2 = Sse41.Blend(vf2, vfa2, HWIntrinsics.BlendMaskAlpha);
					vf3 = Sse41.Blend(vf3, vfa3, HWIntrinsics.BlendMaskAlpha);

					Sse.Store(op, vf0);
					Sse.Store(op + Vector128<float>.Count, vf1);
					Sse.Store(op + Vector128<float>.Count * 2, vf2);
					Sse.Store(op + Vector128<float>.Count * 3, vf3);
					op += Vector128<float>.Count * 4;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector128<byte>.Count)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<byte, float>(offs));
					Sse2.Store(ip, vlast);
					goto LoopTop;
				}
			}
		}
#endif

		private static void convertScalar(byte* ip, byte* ipe, float* op)
		{
			fixed (float* atstart = &LookupTables.Alpha.GetDataRef())
			{
				float* at = atstart;

				while (ip < ipe)
				{
					float o0 = at[(nuint)ip[0]];
					float o1 = at[(nuint)ip[1]];
					float o2 = at[(nuint)ip[2]];
					float o3 = at[(nuint)ip[3]];
					ip += 4;

					op[0] = o0 * o3;
					op[1] = o1 * o3;
					op[2] = o2 * o3;
					op[3] = o3;
					op += 4;
				}
			}
		}
	}

	private sealed unsafe class WideningImpl3X : IConversionProcessor<byte, float>
	{
		void IConversionProcessor.ConvertLine(byte* istart, byte* ostart, nint cb)
		{
			fixed (float* atstart = &LookupTables.Alpha.GetDataRef())
			{
				byte* ip = istart, ipe = istart + cb;
				float* op = (float*)ostart, at = atstart;

#if HWINTRINSICS
				if (Avx2.IsSupported)
				{
					var vscale = Vector256.Create(1f / byte.MaxValue);
					var vmaskp = Avx.LoadVector256((int*)HWIntrinsics.PermuteMask3To3xChan.GetAddressOf());

					ipe -= Vector256<byte>.Count * 3 / 4 + 2; // +2 accounts for the overrun on the last read
					while (ip <= ipe)
					{
						var vi0 = Avx2.ConvertToVector256Int32(ip);
						var vi1 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 3 / 4);
						var vi2 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 6 / 4);
						var vi3 = Avx2.ConvertToVector256Int32(ip + Vector256<int>.Count * 9 / 4);
						ip += Vector256<byte>.Count * 3 / 4;

						vi0 = Avx2.PermuteVar8x32(vi0, vmaskp);
						vi1 = Avx2.PermuteVar8x32(vi1, vmaskp);
						vi2 = Avx2.PermuteVar8x32(vi2, vmaskp);
						vi3 = Avx2.PermuteVar8x32(vi3, vmaskp);

						var vf0 = Avx.ConvertToVector256Single(vi0);
						var vf1 = Avx.ConvertToVector256Single(vi1);
						var vf2 = Avx.ConvertToVector256Single(vi2);
						var vf3 = Avx.ConvertToVector256Single(vi3);

						vf0 = Avx.Multiply(vf0, vscale);
						vf1 = Avx.Multiply(vf1, vscale);
						vf2 = Avx.Multiply(vf2, vscale);
						vf3 = Avx.Multiply(vf3, vscale);

						Avx.Store(op, vf0);
						Avx.Store(op + Vector256<float>.Count, vf1);
						Avx.Store(op + Vector256<float>.Count * 2, vf2);
						Avx.Store(op + Vector256<float>.Count * 3, vf3);
						op += Vector256<float>.Count * 4;
					}
					ipe += Vector256<byte>.Count * 3 / 4 + 2;
				}
				else if (Sse41.IsSupported)
				{
					var vscale = Vector128.Create(1f / byte.MaxValue);

					ipe -= Vector128<byte>.Count * 3 / 4 + 1; // +1 accounts for the overrun on the last read
					while (ip <= ipe)
					{
						var vi0 = Sse41.ConvertToVector128Int32(ip);
						var vi1 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 3 / 4);
						var vi2 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 6 / 4);
						var vi3 = Sse41.ConvertToVector128Int32(ip + Vector128<int>.Count * 9 / 4);
						ip += Vector128<byte>.Count * 3 / 4;

						var vf0 = Sse2.ConvertToVector128Single(vi0);
						var vf1 = Sse2.ConvertToVector128Single(vi1);
						var vf2 = Sse2.ConvertToVector128Single(vi2);
						var vf3 = Sse2.ConvertToVector128Single(vi3);

						vf0 = Sse.Multiply(vf0, vscale);
						vf1 = Sse.Multiply(vf1, vscale);
						vf2 = Sse.Multiply(vf2, vscale);
						vf3 = Sse.Multiply(vf3, vscale);

						Sse.Store(op, vf0);
						Sse.Store(op + Vector128<float>.Count, vf1);
						Sse.Store(op + Vector128<float>.Count * 2, vf2);
						Sse.Store(op + Vector128<float>.Count * 3, vf3);
						op += Vector128<float>.Count * 4;
					}
					ipe += Vector128<byte>.Count * 3 / 4 + 1;
				}
#endif

				float z = Vector4.Zero.X;
				while (ip < ipe)
				{
					float o0 = at[(nuint)ip[0]];
					float o1 = at[(nuint)ip[1]];
					float o2 = at[(nuint)ip[2]];
					ip += 3;

					op[0] = o0;
					op[1] = o1;
					op[2] = o2;
					op[3] = z;
					op += 4;
				}
			}
		}
	}

	private sealed unsafe class NarrowingImpl(int offset, int scale) : IConversionProcessor<float, byte>
	{
		private readonly float scale = scale;
		private readonly short offset = (short)offset;

		void IConversionProcessor.ConvertLine(byte* istart, byte* ostart, nint cb)
		{
			float* ip = (float*)istart, ipe = (float*)(istart + cb);
			byte* op = ostart;

#if HWINTRINSICS
			if (HWIntrinsics.IsSupported && cb >= HWIntrinsics.VectorCount<byte>() * 4)
				convertIntrinsic(ip, ipe, op);
			else
#endif
			if (!HWIntrinsics.IsSupported && cb >= Vector<byte>.Count * 4)
				convertVector(ip, ipe, op);
			else
				convertScalar(ip, ipe, op);
		}

#if HWINTRINSICS
		private void convertIntrinsic(float* ip, float* ipe, byte* op)
		{
			if (Avx2.IsSupported)
			{
				var vscale = Vector256.Create(scale);
				var voffs = Vector256.Create(offset);
				var vmaskp = Avx.LoadVector256((int*)HWIntrinsics.PermuteMaskDeinterleave8x32.GetAddressOf());
				ipe -= Vector256<float>.Count * 4;

				LoopTop:
				do
				{
					var vf0 = Avx.Multiply(vscale, Avx.LoadVector256(ip));
					var vf1 = Avx.Multiply(vscale, Avx.LoadVector256(ip + Vector256<float>.Count));
					var vf2 = Avx.Multiply(vscale, Avx.LoadVector256(ip + Vector256<float>.Count * 2));
					var vf3 = Avx.Multiply(vscale, Avx.LoadVector256(ip + Vector256<float>.Count * 3));
					ip += Vector256<float>.Count * 4;

					var vi0 = Avx.ConvertToVector256Int32(vf0);
					var vi1 = Avx.ConvertToVector256Int32(vf1);
					var vi2 = Avx.ConvertToVector256Int32(vf2);
					var vi3 = Avx.ConvertToVector256Int32(vf3);

					var vs0 = Avx2.PackSignedSaturate(vi0, vi1);
					var vs1 = Avx2.PackSignedSaturate(vi2, vi3);

					vs0 = Avx2.Add(vs0, voffs);
					vs1 = Avx2.Add(vs1, voffs);

					var vb0 = Avx2.PackUnsignedSaturate(vs0, vs1);
					vb0 = Avx2.PermuteVar8x32(vb0.AsInt32(), vmaskp).AsByte();

					Avx.Store(op, vb0);
					op += Vector256<byte>.Count;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector256<float>.Count * 4)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<float, byte>(offs));
					goto LoopTop;
				}
			}
			else // Sse2
			{
				var vscale = Vector128.Create(scale);
				var voffs = Vector128.Create(offset);
				ipe -= Vector128<float>.Count * 4;

				LoopTop:
				do
				{
					var vf0 = Sse.Multiply(Sse.LoadVector128(ip), vscale);
					var vf1 = Sse.Multiply(Sse.LoadVector128(ip + Vector128<float>.Count), vscale);
					var vf2 = Sse.Multiply(Sse.LoadVector128(ip + Vector128<float>.Count * 2), vscale);
					var vf3 = Sse.Multiply(Sse.LoadVector128(ip + Vector128<float>.Count * 3), vscale);
					ip += Vector128<float>.Count * 4;

					var vi0 = Sse2.ConvertToVector128Int32(vf0);
					var vi1 = Sse2.ConvertToVector128Int32(vf1);
					var vi2 = Sse2.ConvertToVector128Int32(vf2);
					var vi3 = Sse2.ConvertToVector128Int32(vf3);

					var vs0 = Sse2.PackSignedSaturate(vi0, vi1);
					var vs1 = Sse2.PackSignedSaturate(vi2, vi3);

					vs0 = Sse2.Add(vs0, voffs);
					vs1 = Sse2.Add(vs1, voffs);

					var vb0 = Sse2.PackUnsignedSaturate(vs0, vs1);

					Sse2.Store(op, vb0);
					op += Vector128<byte>.Count;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector128<float>.Count * 4)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<float, byte>(offs));
					goto LoopTop;
				}
			}
		}
#endif

		private void convertVector(float* ip, float* ipe, byte* op)
		{
#if VECTOR_CONVERT
			int unrollCount = Vector<byte>.Count;
			var vmin = new Vector<short>(byte.MinValue);
			var vmax = new Vector<short>(byte.MaxValue);
#else
			int unrollCount = VectorF.Count;
			var vmin = new VectorF(byte.MinValue);
			var vmax = new VectorF(byte.MaxValue);
#endif
			var vscale = new VectorF(scale);
			var vround = new VectorF(offset + 0.5f);
			ipe -= unrollCount;

			LoopTop:
			do
			{
#if VECTOR_CONVERT
				var vf0 = Unsafe.ReadUnaligned<VectorF>(ip);
				var vf1 = Unsafe.ReadUnaligned<VectorF>(ip + VectorF.Count);
				var vf2 = Unsafe.ReadUnaligned<VectorF>(ip + VectorF.Count * 2);
				var vf3 = Unsafe.ReadUnaligned<VectorF>(ip + VectorF.Count * 3);

				vf0 = vf0 * vscale + vround;
				vf1 = vf1 * vscale + vround;
				vf2 = vf2 * vscale + vround;
				vf3 = vf3 * vscale + vround;

				var vi0 = Vector.ConvertToInt32(vf0);
				var vi1 = Vector.ConvertToInt32(vf1);
				var vi2 = Vector.ConvertToInt32(vf2);
				var vi3 = Vector.ConvertToInt32(vf3);

				var vs0 = Vector.Narrow(vi0, vi1);
				var vs1 = Vector.Narrow(vi2, vi3);

				vs0 = vs0.Clamp(vmin, vmax);
				vs1 = vs1.Clamp(vmin, vmax);

				var vb = Vector.Narrow(Vector.AsVectorUInt16(vs0), Vector.AsVectorUInt16(vs1));
				Unsafe.WriteUnaligned(op, vb);
#else
				var v = Unsafe.ReadUnaligned<VectorF>(ip) * vscale + vround;
				v = v.Clamp(vmin, vmax);

				op[0] = (byte)v[0];
				op[1] = (byte)v[1];
				op[2] = (byte)v[2];
				op[3] = (byte)v[3];

				if (VectorF.Count == 8)
				{
					op[4] = (byte)v[4];
					op[5] = (byte)v[5];
					op[6] = (byte)v[6];
					op[7] = (byte)v[7];
				}
#endif
				ip += unrollCount;
				op += unrollCount;
			}
			while (ip <= ipe);

			if (ip < ipe + unrollCount)
			{
				nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
				ip = UnsafeUtil.SubtractOffset(ip, offs);
				op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<float, byte>(offs));
				goto LoopTop;
			}
		}

		private void convertScalar(float* ip, float* ipe, byte* op)
		{
			float scal = scale;
			float offs = offset + 0.5f;

			while (ip < ipe)
			{
				op[0] = ClampToByte((int)(ip[0] * scale + offs));
				ip++;
				op++;
			}
		}
	}

	private sealed unsafe class NarrowingImpl3A : IConversionProcessor<float, byte>
	{
		void IConversionProcessor.ConvertLine(byte* istart, byte* ostart, nint cb)
		{
			float* ip = (float*)istart, ipe = (float*)(istart + cb);
			byte* op = ostart;

#if HWINTRINSICS
			if (Sse41.IsSupported && cb >= HWIntrinsics.VectorCount<byte>() * 4)
				convertIntrinsic(ip, ipe, op);
			else
#endif
				convertScalar(ip, ipe, op);
		}

#if HWINTRINSICS
		private static unsafe void convertIntrinsic(float* ip, float* ipe, byte* op)
		{
			if (Avx2.IsSupported)
			{
				var vmin = Vector256.Create(0.5f / byte.MaxValue);
				var vscale = Vector256.Create((float)byte.MaxValue);

				var vmaskp = Avx.LoadVector256((int*)HWIntrinsics.PermuteMaskDeinterleave8x32.GetAddressOf());
				ipe -= Vector256<float>.Count * 4;

				LoopTop:
				do
				{
					var vf0 = Avx.LoadVector256(ip);
					var vf1 = Avx.LoadVector256(ip + Vector256<float>.Count);
					var vf2 = Avx.LoadVector256(ip + Vector256<float>.Count * 2);
					var vf3 = Avx.LoadVector256(ip + Vector256<float>.Count * 3);
					ip += Vector256<float>.Count * 4;

					var vfa0 = Avx.Shuffle(vf0, vf0, HWIntrinsics.ShuffleMaskAlpha);
					var vfa1 = Avx.Shuffle(vf1, vf1, HWIntrinsics.ShuffleMaskAlpha);
					var vfa2 = Avx.Shuffle(vf2, vf2, HWIntrinsics.ShuffleMaskAlpha);
					var vfa3 = Avx.Shuffle(vf3, vf3, HWIntrinsics.ShuffleMaskAlpha);

					var vfr0 = Avx.AndNot(Avx.CompareLessThan(vfa0, vmin), Avx.Reciprocal(vfa0));
					var vfr1 = Avx.AndNot(Avx.CompareLessThan(vfa1, vmin), Avx.Reciprocal(vfa1));
					var vfr2 = Avx.AndNot(Avx.CompareLessThan(vfa2, vmin), Avx.Reciprocal(vfa2));
					var vfr3 = Avx.AndNot(Avx.CompareLessThan(vfa3, vmin), Avx.Reciprocal(vfa3));

					vf0 = Avx.Multiply(vf0, vfr0);
					vf1 = Avx.Multiply(vf1, vfr1);
					vf2 = Avx.Multiply(vf2, vfr2);
					vf3 = Avx.Multiply(vf3, vfr3);

					vf0 = Avx.Blend(vf0, vfa0, HWIntrinsics.BlendMaskAlpha);
					vf1 = Avx.Blend(vf1, vfa1, HWIntrinsics.BlendMaskAlpha);
					vf2 = Avx.Blend(vf2, vfa2, HWIntrinsics.BlendMaskAlpha);
					vf3 = Avx.Blend(vf3, vfa3, HWIntrinsics.BlendMaskAlpha);

					vf0 = Avx.Multiply(vf0, vscale);
					vf1 = Avx.Multiply(vf1, vscale);
					vf2 = Avx.Multiply(vf2, vscale);
					vf3 = Avx.Multiply(vf3, vscale);

					var vi0 = Avx.ConvertToVector256Int32(vf0);
					var vi1 = Avx.ConvertToVector256Int32(vf1);
					var vi2 = Avx.ConvertToVector256Int32(vf2);
					var vi3 = Avx.ConvertToVector256Int32(vf3);

					var vs0 = Avx2.PackSignedSaturate(vi0, vi1);
					var vs1 = Avx2.PackSignedSaturate(vi2, vi3);

					var vb0 = Avx2.PackUnsignedSaturate(vs0, vs1);
					vb0 = Avx2.PermuteVar8x32(vb0.AsInt32(), vmaskp).AsByte();

					Avx.Store(op, vb0);
					op += Vector256<byte>.Count;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector256<float>.Count * 4)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<float, byte>(offs));
					goto LoopTop;
				}
			}
			else // Sse41
			{
				var vmin = Vector128.Create(0.5f / byte.MaxValue);
				var vscale = Vector128.Create((float)byte.MaxValue);
				ipe -= Vector128<float>.Count * 4;

				LoopTop:
				do
				{
					var vf0 = Sse.LoadVector128(ip);
					var vf1 = Sse.LoadVector128(ip + Vector128<float>.Count);
					var vf2 = Sse.LoadVector128(ip + Vector128<float>.Count * 2);
					var vf3 = Sse.LoadVector128(ip + Vector128<float>.Count * 3);
					ip += Vector128<float>.Count * 4;

					var vfa0 = Sse.Shuffle(vf0, vf0, HWIntrinsics.ShuffleMaskAlpha);
					var vfa1 = Sse.Shuffle(vf1, vf1, HWIntrinsics.ShuffleMaskAlpha);
					var vfa2 = Sse.Shuffle(vf2, vf2, HWIntrinsics.ShuffleMaskAlpha);
					var vfa3 = Sse.Shuffle(vf3, vf3, HWIntrinsics.ShuffleMaskAlpha);

					var vfr0 = Sse.AndNot(Sse.CompareLessThan(vfa0, vmin), Sse.Reciprocal(vfa0));
					var vfr1 = Sse.AndNot(Sse.CompareLessThan(vfa1, vmin), Sse.Reciprocal(vfa1));
					var vfr2 = Sse.AndNot(Sse.CompareLessThan(vfa2, vmin), Sse.Reciprocal(vfa2));
					var vfr3 = Sse.AndNot(Sse.CompareLessThan(vfa3, vmin), Sse.Reciprocal(vfa3));

					vf0 = Sse.Multiply(vf0, vfr0);
					vf1 = Sse.Multiply(vf1, vfr1);
					vf2 = Sse.Multiply(vf2, vfr2);
					vf3 = Sse.Multiply(vf3, vfr3);

					vf0 = Sse41.Blend(vf0, vfa0, HWIntrinsics.BlendMaskAlpha);
					vf1 = Sse41.Blend(vf1, vfa1, HWIntrinsics.BlendMaskAlpha);
					vf2 = Sse41.Blend(vf2, vfa2, HWIntrinsics.BlendMaskAlpha);
					vf3 = Sse41.Blend(vf3, vfa3, HWIntrinsics.BlendMaskAlpha);

					vf0 = Sse.Multiply(vf0, vscale);
					vf1 = Sse.Multiply(vf1, vscale);
					vf2 = Sse.Multiply(vf2, vscale);
					vf3 = Sse.Multiply(vf3, vscale);

					var vi0 = Sse2.ConvertToVector128Int32(vf0);
					var vi1 = Sse2.ConvertToVector128Int32(vf1);
					var vi2 = Sse2.ConvertToVector128Int32(vf2);
					var vi3 = Sse2.ConvertToVector128Int32(vf3);

					var vs0 = Sse2.PackSignedSaturate(vi0, vi1);
					var vs1 = Sse2.PackSignedSaturate(vi2, vi3);

					var vb0 = Sse2.PackUnsignedSaturate(vs0, vs1);

					Sse2.Store(op, vb0);
					op += Vector128<byte>.Count;
				}
				while (ip <= ipe);

				if (ip < ipe + Vector128<float>.Count * 4)
				{
					nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
					ip = UnsafeUtil.SubtractOffset(ip, offs);
					op = UnsafeUtil.SubtractOffset(op, UnsafeUtil.ConvertOffset<float, byte>(offs));
					goto LoopTop;
				}
			}
		}
#endif

		private static void convertScalar(float* ip, float* ipe, byte* op)
		{
			float fmax = new Vector4(byte.MaxValue).X, fround = new Vector4(0.5f).X, fmin = fround / fmax;

			while (ip < ipe)
			{
				float f3 = ip[3];
				if (f3 < fmin)
				{
					*(uint*)op = 0;
				}
				else
				{
					float f3i = fmax / f3;
					byte o0 = ClampToByte((int)(ip[0] * f3i + fround));
					byte o1 = ClampToByte((int)(ip[1] * f3i + fround));
					byte o2 = ClampToByte((int)(ip[2] * f3i + fround));
					byte o3 = ClampToByte((int)(f3 * fmax + fround));
					op[0] = o0;
					op[1] = o1;
					op[2] = o2;
					op[3] = o3;
				}

				ip += 4;
				op += 4;
			}
		}
	}

	private sealed unsafe class NarrowingImpl3X : IConversionProcessor<float, byte>
	{
		void IConversionProcessor.ConvertLine(byte* istart, byte* ostart, nint cb)
		{
			float* ip = (float*)istart, ipe = (float*)(istart + cb);
			byte* op = ostart;

#if HWINTRINSICS
			if (Avx2.IsSupported && ipe >= ip + Vector256<byte>.Count)
			{
				var vscale = Vector256.Create((float)byte.MaxValue);
				var vmaskp = Avx.LoadVector256((int*)HWIntrinsics.PermuteMaskDeinterleave8x32.GetAddressOf());
				var vmaskq = Avx.LoadVector256((int*)HWIntrinsics.PermuteMask3xTo3Chan.GetAddressOf());
				var vmasks = Avx2.BroadcastVector128ToVector256(HWIntrinsics.ShuffleMask3xTo3Chan.GetAddressOf());

				ipe -= Vector256<float>.Count * 4;
				while (true)
				{
					var vf0 = Avx.Multiply(Avx.LoadVector256(ip), vscale);
					var vf1 = Avx.Multiply(Avx.LoadVector256(ip + Vector256<float>.Count), vscale);
					var vf2 = Avx.Multiply(Avx.LoadVector256(ip + Vector256<float>.Count * 2), vscale);
					var vf3 = Avx.Multiply(Avx.LoadVector256(ip + Vector256<float>.Count * 3), vscale);
					ip += Vector256<float>.Count * 4;

					var vi0 = Avx.ConvertToVector256Int32(vf0);
					var vi1 = Avx.ConvertToVector256Int32(vf1);
					var vi2 = Avx.ConvertToVector256Int32(vf2);
					var vi3 = Avx.ConvertToVector256Int32(vf3);

					var vs0 = Avx2.PackSignedSaturate(vi0, vi1);
					var vs1 = Avx2.PackSignedSaturate(vi2, vi3);

					var vb0 = Avx2.PackUnsignedSaturate(vs0, vs1);
					vb0 = Avx2.PermuteVar8x32(vb0.AsInt32(), vmaskp).AsByte();

					vb0 = Avx2.Shuffle(vb0, vmasks);
					vb0 = Avx2.PermuteVar8x32(vb0.AsInt32(), vmaskq).AsByte();

					if (ip >= ipe)
						goto LastBlock;

					Avx.Store(op, vb0);
					op += Vector256<byte>.Count * 3 / 4;
					continue;

					LastBlock:
					Sse2.Store(op, vb0.GetLower());
					Sse2.StoreScalar((long*)(op + Vector128<byte>.Count), vb0.GetUpper().AsInt64());
					op += Vector256<byte>.Count * 3 / 4;
					break;
				}
				ipe += Vector256<float>.Count * 4;
			}
			else if (Ssse3.IsSupported && ipe >= ip + Vector128<byte>.Count)
			{
				var vscale = Vector128.Create((float)byte.MaxValue);
				var vmasks = Sse2.LoadVector128(HWIntrinsics.ShuffleMask3xTo3Chan.GetAddressOf());

				ipe -= Vector128<float>.Count * 4;
				while (true)
				{
					var vf0 = Sse.Multiply(Sse.LoadVector128(ip), vscale);
					var vf1 = Sse.Multiply(Sse.LoadVector128(ip + Vector128<float>.Count), vscale);
					var vf2 = Sse.Multiply(Sse.LoadVector128(ip + Vector128<float>.Count * 2), vscale);
					var vf3 = Sse.Multiply(Sse.LoadVector128(ip + Vector128<float>.Count * 3), vscale);
					ip += Vector128<float>.Count * 4;

					var vi0 = Sse2.ConvertToVector128Int32(vf0);
					var vi1 = Sse2.ConvertToVector128Int32(vf1);
					var vi2 = Sse2.ConvertToVector128Int32(vf2);
					var vi3 = Sse2.ConvertToVector128Int32(vf3);

					var vs0 = Sse2.PackSignedSaturate(vi0, vi1);
					var vs1 = Sse2.PackSignedSaturate(vi2, vi3);

					var vb0 = Sse2.PackUnsignedSaturate(vs0, vs1);

					vb0 = Ssse3.Shuffle(vb0, vmasks);

					if (ip >= ipe)
						goto LastBlock;

					Sse2.Store(op, vb0);
					op += Vector128<byte>.Count * 3 / 4;
					continue;

					LastBlock:
					var vl0 = vb0.AsUInt64();
					Sse2.StoreScalar((ulong*)op, vl0);
					Sse2.StoreScalar((uint*)(op + sizeof(ulong)), Sse2.UnpackHigh(vl0, vl0).AsUInt32());
					op += Vector128<byte>.Count * 3 / 4;
					break;
				}
				ipe += Vector128<float>.Count * 4;
			}
			else
#endif
			{
				var vmin = new VectorF(byte.MinValue);
				var vmax = new VectorF(byte.MaxValue);
				var vround = new VectorF(0.5f);

				ipe -= VectorF.Count;
				while (ip <= ipe)
				{
					var v = Unsafe.ReadUnaligned<VectorF>(ip) * vmax + vround;
					v = v.Clamp(vmin, vmax);
					ip += VectorF.Count;

#if VECTOR_CONVERT
					var vi = Vector.ConvertToInt32(v);
#else
					var vi = v;
#endif

					op[0] = (byte)vi[0];
					op[1] = (byte)vi[1];
					op[2] = (byte)vi[2];

					if (VectorF.Count == 8)
					{
						op[3] = (byte)vi[4];
						op[4] = (byte)vi[5];
						op[5] = (byte)vi[6];
					}
					op += VectorF.Count - VectorF.Count / 4;
				}
				ipe += VectorF.Count;
			}

			while (ip < ipe)
			{
				op[0] = FixToByte(ip[0]);
				op[1] = FixToByte(ip[1]);
				op[2] = FixToByte(ip[2]);

				ip += 4;
				op += 3;
			}
		}
	}

	public static unsafe class Interpolating
	{
		public static void ConvertFloat(byte* istart, byte* ostart, float* lutstart, int lutmax, nint cb)
		{
			Debug.Assert(istart == ostart);

			float* ip = (float*)istart, ipe = (float*)(istart + cb);
			float* lp = lutstart;

#if HWINTRINSICS
			if (Avx2.IsSupported && cb >= Vector256<byte>.Count)
				convertFloatAvx2(ip, ipe, lp, lutmax);
			else
#endif
				convertFloatScalar(ip, ipe, lp, lutmax);
		}

#if HWINTRINSICS
		private static void convertFloatAvx2(float* ip, float* ipe, float* lp, int lutmax)
		{
			var vlmax = Vector256.Create((float)lutmax);
			var vzero = Vector256<float>.Zero;
			var vione = Vector256.Create(1);
			ipe -= Vector256<float>.Count;

			var vlast = Avx.LoadVector256(ipe);

			LoopTop:
			do
			{
				var vf = Avx.Multiply(vlmax, Avx.LoadVector256(ip));
				vf = Avx.Min(Avx.Max(vzero, vf), vlmax);

				var vi = Avx.ConvertToVector256Int32WithTruncation(vf);
				var vt = Avx.ConvertToVector256Single(vi);

				var vl = Avx2.GatherVector256(lp, vi, sizeof(float));
				var vh = Avx2.GatherVector256(lp, Avx2.Add(vi, vione), sizeof(float));

				vf = HWIntrinsics.Lerp(vl, vh, Avx.Subtract(vf, vt));

				Avx.Store(ip, vf);
				ip += Vector256<float>.Count;
			}
			while (ip <= ipe);

			if (ip < ipe + Vector256<float>.Count)
			{
				nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
				ip = UnsafeUtil.SubtractOffset(ip, offs);
				Avx.Store(ip, vlast);
				goto LoopTop;
			}
		}
#endif

		private static void convertFloatScalar(float* ip, float* ipe, float* lp, int lutmax)
		{
			var vlmax = new Vector4(lutmax);
			var vzero = Vector4.Zero;

			ipe -= 4;
			while (ip <= ipe)
			{
				var vf = (Unsafe.ReadUnaligned<Vector4>(ip) * vlmax).Clamp(vzero, vlmax);

				float f0 = vf.X;
				float f1 = vf.Y;
				float f2 = vf.Z;
				float f3 = vf.W;

				uint i0 = (uint)f0;
				uint i1 = (uint)f1;
				uint i2 = (uint)f2;
				uint i3 = (uint)f3;

				ip[0] = Lerp(lp[i0], lp[i0 + 1], f0 - (int)i0);
				ip[1] = Lerp(lp[i1], lp[i1 + 1], f1 - (int)i1);
				ip[2] = Lerp(lp[i2], lp[i2 + 1], f2 - (int)i2);
				ip[3] = Lerp(lp[i3], lp[i3 + 1], f3 - (int)i3);
				ip += 4;
			}
			ipe += 4;

			float fmin = vzero.X, flmax = vlmax.X;
			while (ip < ipe)
			{
				float f = (*ip * flmax).Clamp(fmin, flmax);
				uint i = (uint)f;

				*ip++ = Lerp(lp[i], lp[i + 1], f - i);
			}
		}

		public static void ConvertFloat3A(byte* istart, byte* ostart, float* lutstart, int lutmax, nint cb)
		{
			Debug.Assert(istart == ostart);

			float* ip = (float*)istart, ipe = (float*)(istart + cb);
			float* lp = lutstart;

#if HWINTRINSICS
			if (Avx2.IsSupported && cb >= Vector256<byte>.Count)
				convertFloat3AAvx2(ip, ipe, lp, lutmax);
			else
#endif
				convertFloat3AScalar(ip, ipe, lp, lutmax);
		}

#if HWINTRINSICS
		private static void convertFloat3AAvx2(float* ip, float* ipe, float* lp, int lutmax)
		{
			var vgmsk = Avx.BroadcastVector128ToVector256((float*)HWIntrinsics.GatherMask3x.GetAddressOf());
			var vgmax = Vector256.Create((float)lutmax);
			var vzero = Vector256<float>.Zero;
			var vfone = Vector256.Create(1f);
			var vione = Vector256.Create(1);
			ipe -= Vector256<float>.Count;

			var vlast = Avx.LoadVector256(ipe);

			LoopTop:
			do
			{
				var vf = Avx.Max(vzero, Avx.LoadVector256(ip));
				var va = Avx.Shuffle(vf, vf, HWIntrinsics.ShuffleMaskAlpha);

				vf = Avx.Multiply(vf, Avx.Multiply(vgmax, Avx.Reciprocal(va)));
				vf = Avx.Min(vf, vgmax);

				var vi = Avx.ConvertToVector256Int32WithTruncation(vf);
				var vt = Avx.ConvertToVector256Single(vi);

				var vl = Avx2.GatherMaskVector256(vfone, lp, vi, vgmsk, sizeof(float));
				var vh = Avx2.GatherMaskVector256(vfone, lp, Avx2.Add(vi, vione), vgmsk, sizeof(float));

				vf = HWIntrinsics.Lerp(vl, vh, Avx.Subtract(vf, vt));
				vf = Avx.Multiply(vf, va);

				Avx.Store(ip, vf);
				ip += Vector256<float>.Count;
			}
			while (ip <= ipe);

			if (ip < ipe + Vector256<float>.Count)
			{
				nuint offs = UnsafeUtil.ByteOffset(ipe, ip);
				ip = UnsafeUtil.SubtractOffset(ip, offs);
				Avx.Store(ip, vlast);
				goto LoopTop;
			}
		}
#endif

		private static void convertFloat3AScalar(float* ip, float* ipe, float* lp, int lutmax)
		{
			var vlmax = new Vector4(lutmax);
			var vzero = Vector4.Zero;
			float famin = new Vector4(1 / 1024f).X;

			while (ip < ipe)
			{
				var vf = Unsafe.ReadUnaligned<Vector4>(ip);

				float f3 = vf.W;
				if (f3 < famin)
				{
					Unsafe.WriteUnaligned(ip, vzero);
				}
				else
				{
					var va = new Vector4(vlmax.X / f3);
					vf = (vf * va).Clamp(vzero, vlmax);

					float f0 = vf.X;
					float f1 = vf.Y;
					float f2 = vf.Z;

					uint i0 = (uint)f0;
					uint i1 = (uint)f1;
					uint i2 = (uint)f2;

					ip[0] = Lerp(lp[i0], lp[i0 + 1], f0 - (int)i0) * f3;
					ip[1] = Lerp(lp[i1], lp[i1 + 1], f1 - (int)i1) * f3;
					ip[2] = Lerp(lp[i2], lp[i2 + 1], f2 - (int)i2) * f3;
					ip[3] = f3;
				}
				ip += 4;
			}
		}
	}
}
