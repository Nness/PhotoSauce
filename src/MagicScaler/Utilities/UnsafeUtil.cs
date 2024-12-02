// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
#if !NET5_0_OR_GREATER
using System.Reflection;
#endif

namespace PhotoSauce.MagicScaler;

internal static unsafe class UnsafeUtil
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref T GetDataRef<T>(this T[] array) =>
#if NET5_0_OR_GREATER
		ref MemoryMarshal.GetArrayDataReference(array);
#elif NETFRAMEWORK
		ref sizeof(nuint) == sizeof(ulong) ? ref getRefOrNull(array) : ref array[0];
#else
		ref getRefOrNull(array);
#endif

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TTo BitCast<TFrom, TTo>(TFrom val) where TFrom : unmanaged where TTo : unmanaged =>
#if NET8_0_OR_GREATER
		Unsafe.BitCast<TFrom, TTo>(val);
#else
		*(TTo*)&val;
#endif

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe TTo BitCastTruncate<TFrom, TTo>(TFrom val) where TFrom : unmanaged where TTo : unmanaged =>
		*(TTo*)&val;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nuint ByteOffset<T>(T* tgt, T* cur) where T : unmanaged =>
		(nuint)(nint)((byte*)cur - (byte*)tgt);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T* SubtractOffset<T>(T* ptr, nuint offset) where T : unmanaged =>
		(T*)((byte*)ptr - (nint)offset);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nuint ConvertOffset<TFrom, TTo>(nuint offset) where TFrom : unmanaged where TTo : unmanaged
	{
		if (sizeof(TFrom) > sizeof(TTo))
			return offset / (nuint)(sizeof(TFrom) / sizeof(TTo));
		else if (sizeof(TFrom) < sizeof(TTo))
			return offset * (nuint)(sizeof(TTo) / sizeof(TFrom));
		else
			return offset;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Guid* GetAddressOf(this in Guid val) => (Guid*)Unsafe.AsPointer(ref Unsafe.AsRef(in val));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* GetAddressOf(this ReadOnlySpan<byte> val) => (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(val));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsIntegerPrimitive<T>() where T : unmanaged =>
		typeof(T) == typeof(sbyte)  ||
		typeof(T) == typeof(byte)   ||
		typeof(T) == typeof(short)  ||
		typeof(T) == typeof(ushort) ||
		typeof(T) == typeof(int)    ||
		typeof(T) == typeof(uint)   ||
		typeof(T) == typeof(long)   ||
		typeof(T) == typeof(uint)   ||
		typeof(T) == typeof(nint)   ||
		typeof(T) == typeof(nuint);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsFloatPrimitive<T>() where T : unmanaged =>
		typeof(T) == typeof(float)  ||
		typeof(T) == typeof(double);

	public static T* NativeAlloc<T>() where T : unmanaged =>
#if NET6_0_OR_GREATER
		(T*)NativeMemory.Alloc((nuint)sizeof(T));
#else
		(T*)Marshal.AllocHGlobal(sizeof(T));
#endif

	public static void* NativeAlloc(nuint cb) =>
#if NET6_0_OR_GREATER
		NativeMemory.Alloc(cb);
#else
		(void*)Marshal.AllocHGlobal((nint)cb);
#endif

	public static void NativeFree(void* p) =>
#if NET6_0_OR_GREATER
		NativeMemory.Free(p);
#else
		Marshal.FreeHGlobal((nint)p);
#endif

	public static int StrLen(byte* p) =>
#if NET6_0_OR_GREATER
		MemoryMarshal.CreateReadOnlySpanFromNullTerminated(p).Length;
#else
		new ReadOnlySpan<byte>(p, int.MaxValue).IndexOf((byte)'\0');
#endif

	public static int WcsLen(char* p) =>
#if NET6_0_OR_GREATER
		MemoryMarshal.CreateReadOnlySpanFromNullTerminated(p).Length;
#else
		new ReadOnlySpan<char>(p, int.MaxValue).IndexOf('\0');
#endif

	public static void* AllocateTypeAssociatedMemory(Type type, int cb) =>
#if NET5_0_OR_GREATER
		(void*)RuntimeHelpers.AllocateTypeAssociatedMemory(type, cb);
#else
		(void*)Marshal.AllocHGlobal(cb);
#endif

#if !NET5_0_OR_GREATER
	public static T CreateMethodDelegate<T>(this Type t, string method) where T : Delegate =>
		(T)t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).CreateDelegate(typeof(T), null);

	private static ref T getRefOrNull<T>(this T[] array)
	{
		ref T ar = ref Unsafe.AsRef<T>(null);
		if (0 < (uint)array.Length)
			ar = ref array[0];

		return ref ar;
	}
#endif
}
