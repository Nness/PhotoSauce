// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Drawing;
using System.Runtime.CompilerServices;

using PhotoSauce.MagicScaler;
using PhotoSauce.MagicScaler.Converters;
using PhotoSauce.Interop.Libpng;
using static PhotoSauce.Interop.Libpng.Libpng;

namespace PhotoSauce.NativeCodecs.Libpng;

internal sealed unsafe class PngEncoder : IAnimatedImageEncoder
{
	private readonly IPngEncoderOptions options;

	private ps_png_struct* handle;
	private bool written;
	private AnimationContainer? animation;

	public static PngEncoder Create(Stream outStream, IEncoderOptions? pngOptions) => new(outStream, pngOptions);

	private PngEncoder(Stream outStream, IEncoderOptions? pngOptions)
	{
		options = pngOptions as IPngEncoderOptions ?? PngEncoderOptions.Default;

		var stream = StreamWrapper.Wrap(outStream);
		if (stream is null)
			ThrowHelper.ThrowOutOfMemory();

		handle = PngFactory.CreateEncoder();
		if (handle is null)
		{
			StreamWrapper.Free(stream);
			ThrowHelper.ThrowOutOfMemory();
		}

		var iod = handle->io_ptr;
		iod->stream_handle = (nint)stream;
		iod->write_callback = PngCallbacks.Write;
	}

	public void WriteAnimationMetadata(IMetadataSource metadata)
	{
		if (!metadata.TryGetMetadata<AnimationContainer>(out var anicnt))
			anicnt = default;

		animation = anicnt;
	}

	public void WriteFrame(IPixelSource source, IMetadataSource metadata, Rectangle sourceArea)
	{
		var area = sourceArea == default ? PixelArea.FromSize(source.Width, source.Height) : ((PixelArea)sourceArea);
		if (source.Width > PNG_USER_WIDTH_MAX || area.Height > PNG_USER_HEIGHT_MAX)
			throw new NotSupportedException($"Image too large.  This encoder supports a max of {PNG_USER_WIDTH_MAX}x{PNG_USER_HEIGHT_MAX} pixels.");

		var srcfmt = PixelFormat.FromGuid(source.Format);
		int pngfmt =
			srcfmt == PixelFormat.Grey8 ? PNG_COLOR_TYPE_GRAY :
			srcfmt == PixelFormat.Rgb24 ? PNG_COLOR_TYPE_RGB :
			srcfmt == PixelFormat.Rgba32 ? PNG_COLOR_TYPE_RGBA :
			srcfmt == PixelFormat.Indexed8 ? PNG_COLOR_TYPE_PALETTE :
			throw new NotSupportedException("Image format not supported.");

		if (pngfmt == PNG_COLOR_TYPE_PALETTE && animation.HasValue)
			throw new NotSupportedException("Animation is not supported for indexed PNG.");

		if (!written)
		{
			var (width, height) = animation.HasValue ? (animation.Value.ScreenWidth, animation.Value.ScreenHeight) : (area.Width, area.Height);
			writeHeader(pngfmt, width, height, source, metadata);
		}

		if (animation.HasValue)
		{
			if (!metadata.TryGetMetadata<AnimationFrame>(out var anifrm))
				anifrm = AnimationFrame.Default;

			var duration = anifrm.Duration;
			if (duration.Numerator > ushort.MaxValue || duration.Denominator > ushort.MaxValue)
				duration = duration.NormalizeTo(100);

			byte disposal = (byte)MathUtil.Clamp((int)anifrm.Disposal - 1, (int)PNG_DISPOSE_OP_NONE, (int)PNG_DISPOSE_OP_PREVIOUS);
			byte blend = anifrm.Blend == AlphaBlendMethod.Source ? (byte)PNG_BLEND_OP_SOURCE : (byte)PNG_BLEND_OP_OVER;

			checkResult(PngWriteFrameHead(handle, (uint)area.Width, (uint)area.Height, (uint)anifrm.OffsetLeft, (uint)anifrm.OffsetTop, (ushort)duration.Numerator, (ushort)duration.Denominator, disposal, blend));
			writePixels(source, area);
			checkResult(PngWriteFrameTail(handle));
		}
		else
		{
			if (written)
				throw new InvalidOperationException("An image frame has already been written, and this encoder is not configured for multiple frames.");

			writePixels(source, area);
		}

		written = true;
	}

	public void Commit()
	{
		if (!written)
			throw new InvalidOperationException("An image frame has not been written.");

		checkResult(PngWriteIend(handle));
	}

	private void writeIccp(IMetadataSource metadata)
	{
		if (!metadata.TryGetMetadata<ColorProfileMetadata>(out var prof))
			return;

		if (prof.Profile == ColorProfile.sRGB)
		{
			checkResult(PngWriteSrgb(handle));
			return;
		}

		byte[] embed = prof.Embed;
		fixed (byte* bp = &embed.GetDataRef())
			checkResult(PngWriteIccp(handle, bp));
	}

	private void writeExif(IMetadataSource metadata)
	{
		if (!metadata.TryGetMetadata<ResolutionMetadata>(out var remd) || !remd.IsValid)
			remd = ResolutionMetadata.Default;

		remd = remd.ToDpm();
		checkResult(PngWritePhys(handle, (uint)Math.Round((double)remd.ResolutionX), (uint)Math.Round((double)remd.ResolutionY)));

		var orient = Orientation.Normal;
		if (metadata.TryGetMetadata<OrientationMetadata>(out var ormd))
			orient = ormd.Orientation.Clamp();

		if (orient == Orientation.Normal)
			return;

		using var exif = ExifWriter.Create(1, 0);
		exif.Write(ExifTags.Tiff.Orientation, ExifType.Short, (short)orient);
		exif.Finish();

		var exifspan = exif.Span;
		fixed (byte* bp = exifspan)
			checkResult(PngWriteExif(handle, bp, exifspan.Length));
	}

	private void writeHeader(int pngfmt, int width, int height, IPixelSource src, IMetadataSource meta)
	{
		int filter = pngfmt == PNG_COLOR_TYPE_PALETTE ? PNG_FILTER_VALUE_NONE : PNG_ALL_FILTERS;
		if (options.Filter is > PngFilter.Unspecified and < PngFilter.Adaptive)
			filter = (int)options.Filter - 1;

		checkResult(PngWriteSig(handle));
		checkResult(PngSetFilter(handle, filter));
		checkResult(PngSetCompressionLevel(handle, 5));
		checkResult(PngWriteIhdr(handle, (uint)width, (uint)height, 8, pngfmt, options.Interlace ? PNG_INTERLACE_ADAM7 : PNG_INTERLACE_NONE));

		if (src is IIndexedPixelSource idxs)
		{
			var pal = idxs.Palette;
			fixed (uint* ppal = pal)
			{
				using (var palbuf = BufferPool.RentLocal<byte>(pal.Length * 4))
				fixed (byte* pp = palbuf)
				{
					Unsafe.CopyBlock(pp, ppal, (uint)palbuf.Length);

					Swizzlers<byte>.GetSwapConverter(4, 3).ConvertLine(pp, pp, palbuf.Length);
					checkResult(PngWritePlte(handle, (png_color_struct*)pp, pal.Length));
				}

				if (idxs.HasAlpha())
				{
					using var trnbuf = BufferPool.RentLocal<byte>(pal.Length);
					fixed (byte* pt = trnbuf)
					{
						Swizzlers<byte>.AlphaExtractor.ConvertLine((byte*)ppal, pt, pal.Length * 4);
						checkResult(PngWriteTrns(handle, pt, pal.Length));
					}
				}
			}
		}

		writeIccp(meta);
		writeExif(meta);

		if (animation.HasValue)
			checkResult(PngWriteActl(handle, (uint)animation.Value.FrameCount, (uint)animation.Value.LoopCount));
	}

	private void writePixels(IPixelSource src, PixelArea area)
	{
		var srcfmt = PixelFormat.FromGuid(src.Format);
		int stride = MathUtil.PowerOfTwoCeiling(area.Width * srcfmt.BytesPerPixel, sizeof(uint));

		using var buff = BufferPool.RentLocalAligned<byte>(checked(stride * (options.Interlace ? area.Height : 1)));
		var span = buff.Span;

		fixed (byte* pbuf = buff)
		{
			if (options.Interlace)
			{
				using var lines = BufferPool.RentLocal<nint>(area.Height);
				var lspan = lines.Span;
				for (int i = 0; i < lspan.Length; i++)
					lspan[i] = (nint)(pbuf + i * stride);

				fixed (nint* plines = lines)
				{
					src.CopyPixels(area, stride, span);
					checkResult(PngWriteImage(handle, (byte**)plines));
				}
			}
			else
			{
				for (int y = 0; y < area.Height; y++)
				{
					src.CopyPixels(area.Slice(y, 1), stride, span);
					checkResult(PngWriteRow(handle, pbuf));
				}
			}
		}
	}

	private void checkResult(int res)
	{
		handle->io_ptr->Stream->ThrowIfExceptional();

		if (res == FALSE)
			throw new InvalidOperationException($"{nameof(Libpng)} encoder failed. {new string(PngGetLastError(handle))}");
	}

	private void dispose(bool disposing)
	{
		if (handle is null)
			return;

		StreamWrapper.Free(handle->io_ptr->Stream);
		PngDestroyWrite(handle);
		handle = null;

		if (disposing)
			GC.SuppressFinalize(this);
	}

	public void Dispose() => dispose(true);

	~PngEncoder()
	{
		ThrowHelper.ThrowIfFinalizerExceptionsEnabled(nameof(PngEncoder));

		dispose(false);
	}
}
