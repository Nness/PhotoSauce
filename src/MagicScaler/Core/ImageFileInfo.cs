// Copyright © Clinton Ingram and Contributors.  Licensed under the MIT License.

using System;
using System.IO;
using System.Collections.Generic;

using TerraFX.Interop.Windows;

using PhotoSauce.Interop.Wic;

namespace PhotoSauce.MagicScaler;

/// <summary>Represents basic information about an image container.</summary>
public sealed class ImageFileInfo
{
	/// <summary>Represents basic information about an image frame within a container.</summary>
	/// <param name="width">The width of the image frame in pixels.</param>
	/// <param name="height">The height of the image frame in pixels.</param>
	/// <param name="hasAlpha">True if the image frame contains transparency data, otherwise false.</param>
	/// <param name="orientation">The Exif orientation associated with the image frame.</param>
	public readonly struct FrameInfo(int width, int height, bool hasAlpha, Orientation orientation)
	{
		/// <summary>The width of the image frame in pixels.</summary>
		public int Width { get; } = width;
		/// <summary>The height of the image frame in pixels.</summary>
		public int Height { get; } = height;
		/// <summary>True if the image frame contains transparency data, otherwise false.</summary>
		public bool HasAlpha { get; } = hasAlpha;
		/// <summary>
		/// The stored <a href="https://magnushoff.com/articles/jpeg-orientation/">Exif orientation</a> for the image frame.
		/// The <see cref="Width" /> and <see cref="Height" /> values reflect the corrected orientation, not the stored orientation.
		/// </summary>
		public Orientation ExifOrientation { get; } = orientation;
	}

	/// <summary>The size of the image container in bytes.</summary>
	public long FileSize { get; }
	/// <summary>The last modified date of the image container, if applicable.</summary>
	public DateTime FileDate { get; }

	/// <summary>The MIME type of the image container.</summary>
	public string? MimeType { get; }
	/// <summary>One or more <see cref="FrameInfo" /> instances describing each image frame in the container.</summary>
	public IReadOnlyList<FrameInfo> Frames { get; }

	/// <summary>Constructs a new <see cref="ImageFileInfo" /> instance with a single frame of the specified <paramref name="width" /> and <paramref name="height" />.</summary>
	/// <param name="width">The width of the image frame in pixels.</param>
	/// <param name="height">The height of the image frame in pixels.</param>
	public ImageFileInfo(int width, int height) => Frames = [ new FrameInfo(width, height, false, Orientation.Normal) ];

	/// <summary>Constructs a new <see cref="ImageFileInfo" /> instance the specified values.</summary>
	/// <param name="mimeType">The MIME type of the image container.</param>
	/// <param name="frames">A list containing one <see cref="FrameInfo" /> per image frame in the container.</param>
	/// <param name="fileSize">The size in bytes of the image file.</param>
	/// <param name="fileDate">The last modified date of the image file.</param>
	public ImageFileInfo(string? mimeType, IReadOnlyList<FrameInfo> frames, long fileSize, DateTime fileDate)
	{
		MimeType = mimeType;
		Frames = frames;
		FileSize = fileSize;
		FileDate = fileDate;
	}

	/// <summary>Constructs a new <see cref="ImageFileInfo" /> instance by reading the metadata from an image file header.</summary>
	/// <param name="imgPath">The path to the image file.</param>
	public static ImageFileInfo Load(string imgPath)
	{
		ThrowHelper.ThrowIfNullOrEmpty(imgPath);

		var fi = new FileInfo(imgPath);
		using var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
		using var bfs = new PoolBufferedStream(fs);
		using var cnt = CodecManager.GetDecoderForStream(bfs);

		return fromContainer(cnt, fi.Length, fi.LastWriteTimeUtc);
	}

	/// <inheritdoc cref="Load(ReadOnlySpan{byte}, DateTime)" />
	public static ImageFileInfo Load(ReadOnlySpan<byte> imgBuffer) => Load(imgBuffer, DateTime.MinValue);

	/// <summary>Constructs a new <see cref="ImageFileInfo" /> instance by reading the metadata from an image file contained in a <see cref="ReadOnlySpan{T}" />.</summary>
	/// <param name="imgBuffer">The buffer containing the image data.</param>
	/// <param name="lastModified">The last modified date of the image container.</param>
	public static unsafe ImageFileInfo Load(ReadOnlySpan<byte> imgBuffer, DateTime lastModified)
	{
		if (imgBuffer.IsEmpty) throw new ArgumentNullException(nameof(imgBuffer));

		fixed (byte* pbBuffer = imgBuffer)
		{
			using var ums = new UnmanagedMemoryStream(pbBuffer, imgBuffer.Length);
			using var cnt = CodecManager.GetDecoderForStream(ums);

			return fromContainer(cnt, imgBuffer.Length, lastModified);
		}
	}

	/// <inheritdoc cref="Load(Stream, DateTime)" />
	public static ImageFileInfo Load(Stream imgStream) => Load(imgStream, DateTime.MinValue);

	/// <summary>Constructs a new <see cref="ImageFileInfo" /> instance by reading the metadata from an image file exposed by a <see cref="Stream" />.</summary>
	/// <param name="imgStream">The stream containing the image data.</param>
	/// <param name="lastModified">The last modified date of the image container.</param>
	public static ImageFileInfo Load(Stream imgStream, DateTime lastModified)
	{
		ThrowHelper.ThrowIfNotValidForInput(imgStream);

		using var bfs = PoolBufferedStream.WrapIfFile(imgStream);
		using var cnt = CodecManager.GetDecoderForStream(bfs ?? imgStream);

		return fromContainer(cnt, imgStream.Length, lastModified);
	}

	private static ImageFileInfo fromContainer(IImageContainer cont, long fileSize, DateTime fileDate)
	{
		var frames = cont is WicGifContainer gif ? getGifFrameInfo(gif) : getFrameInfo(cont);

		return new ImageFileInfo(cont.MimeType, frames, fileSize, fileDate);
	}

	private static unsafe FrameInfo[] getFrameInfo(IImageContainer cont)
	{
		bool animation = false;
		int cwidth = 0, cheight = 0;
		if (cont is IMetadataSource meta && (animation = meta.TryGetMetadata<AnimationContainer>(out var anicnt)))
			(cwidth, cheight) = (anicnt.ScreenWidth, anicnt.ScreenHeight);

		var frames = new FrameInfo[cont.FrameCount];
		for (int i = 0; i < frames.Length; i++)
		{
			using var frame = cont.GetFrame(i);
			var src = frame.PixelSource;

			var pixfmt = PixelFormat.FromGuid(src.Format);
			var (width, height) = animation ? (cwidth, cheight) : (src.Width, src.Height);

			var orient = frame.GetOrientation();
			if (orient.SwapsDimensions())
				(width, height) = (height, width);

			frames[i] = new FrameInfo(width, height, pixfmt.AlphaRepresentation != PixelAlphaRepresentation.None, orient);
		}

		return frames;
	}

	private static unsafe FrameInfo[] getGifFrameInfo(WicGifContainer gif)
	{
		using var cmeta = default(ComPtr<IWICMetadataQueryReader>);
		HRESULT.Check(gif.WicDecoder->GetMetadataQueryReader(cmeta.GetAddressOf()));

		int cwidth = cmeta.Get()->GetValueOrDefault<ushort>(Wic.Metadata.Gif.LogicalScreenWidth);
		int cheight = cmeta.Get()->GetValueOrDefault<ushort>(Wic.Metadata.Gif.LogicalScreenHeight);

		bool alpha = gif.IsAnimation;
		if (!alpha)
		{
			using var frame = default(ComPtr<IWICBitmapFrameDecode>);
			using var fmeta = default(ComPtr<IWICMetadataQueryReader>);
			HRESULT.Check(gif.WicDecoder->GetFrame(0, frame.GetAddressOf()));
			HRESULT.Check(frame.Get()->GetMetadataQueryReader(fmeta.GetAddressOf()));

			alpha = fmeta.Get()->GetValueOrDefault<bool>(Wic.Metadata.Gif.TransparencyFlag);
		}

		var frames = new FrameInfo[gif.FrameCount];
		for (int i = 0; i < frames.Length; i++)
			frames[i] = new FrameInfo(cwidth, cheight, alpha, Orientation.Normal);

		return frames;
	}
}