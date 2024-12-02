// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
#if NETFRAMEWORK
using System.IO;
using System.Runtime.InteropServices;
#endif

using PhotoSauce.MagicScaler;
using static PhotoSauce.Interop.Libheif.Libheif;

namespace PhotoSauce.NativeCodecs.Libheif;

internal static unsafe class HeifFactory
{
	public const string DisplayName = $"{libheif} {LIBHEIF_VERSION}";
	public const string libheif = nameof(libheif);
	public const uint libver = LIBHEIF_NUMERIC_VERSION;

	private static readonly Lazy<bool> dependencyValid = new(() => {
#if NETFRAMEWORK
		// netfx doesn't have RID-based native dependency resolution, so we include a .props
		// file that copies binaries for all supported architectures to the output folder,
		// then make a perfunctory attempt to load the right one before the first P/Invoke.
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			[DllImport("kernel32", ExactSpelling = true)]
			static extern IntPtr LoadLibraryW(ushort* lpLibFileName);

			fixed (char* plib = Path.Combine(typeof(HeifFactory).Assembly.GetArchDirectory(), "heif"))
				LoadLibraryW((ushort*)plib);
		}
#endif

		uint ver = heif_get_version_number();
		if (ver != libver)
			throw new NotSupportedException($"Incorrect {libheif} version was loaded.  Expected 0x{libver:x8}, found 0x{ver:x8}.");

		return true;
	});

	public static void* CreateContext() => dependencyValid.Value ? heif_context_alloc() : default;

	public static string GetMimeType(byte* data, int len) => dependencyValid.Value ? new(heif_get_file_mime_type(data, len)) : "image/heif";
}

/// <inheritdoc cref="WindowsCodecExtensions" />
public static class CodecCollectionExtensions
{
	/// <inheritdoc cref="WindowsCodecExtensions.UseWicCodecs(CodecCollection, WicCodecPolicy)" />
	/// <param name="removeExisting">Remove any decoders already registered that match <see cref="ImageMimeTypes.Heic" />.</param>
	public static void UseLibheif(this CodecCollection codecs, bool removeExisting = true)
	{
		ThrowHelper.ThrowIfNull(codecs);

		if (removeExisting)
		{
			foreach (var codec in codecs.OfType<IImageDecoderInfo>().Where(c => c.MimeTypes.Any(m => m is ImageMimeTypes.Heic or ImageMimeTypes.Avif)).ToList())
				codecs.Remove(codec);
		}

		codecs.Add(new DecoderInfo(
			HeifFactory.DisplayName,
			[ ImageMimeTypes.Heic, ImageMimeTypes.Avif ],
			[ ImageFileExtensions.Heic, ImageFileExtensions.Avif ],
			new ContainerPattern[] {
				new(0, [ 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c' ], [ 0xff, 0xff, 0xff, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff ]),
				new(0, [ 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'v', (byte)'c' ], [ 0xff, 0xff, 0xff, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff ]),
				new(0, [ 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f' ], [ 0xff, 0xff, 0xff, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff ]),
				new(0, [ 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'i', (byte)'f', (byte)'1' ], [ 0xff, 0xff, 0xff, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff ]),
				new(0, [ 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'i', (byte)'a', (byte)'f' ], [ 0xff, 0xff, 0xff, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff ]),
				new(0, [ 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'1', (byte)'p', (byte)'i', (byte)'c' ], [ 0xff, 0xff, 0xff, 0, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff ])
			},
			null,
			HeifContainer.TryLoad
		));
	}
}
