// Copyright © Clinton Ingram and Contributors.  Licensed under the MIT License.

using System;
using System.Drawing;
using System.Runtime.InteropServices;

using TerraFX.Interop;

using PhotoSauce.MagicScaler.Transforms;

namespace PhotoSauce.MagicScaler
{
	internal sealed class AnimationPipelineContext : IDisposable
	{
		public FrameBufferSource? FrameBufferSource;
		public FrameDisposalMethod LastDisposal = FrameDisposalMethod.RestoreBackground;
		public int LastFrame = -1;

		public unsafe void UpdateFrameBuffer(IImageFrame frame, in AnimationContainer anicnt, in AnimationFrame anifrm)
		{
			const int bytesPerPixel = 4;

			var src = frame.PixelSource;
			var fbuff = FrameBufferSource ??= new FrameBufferSource(anicnt.ScreenWidth, anicnt.ScreenHeight, PixelFormat.Bgra32, true);
			var bspan = fbuff.Span;

			fbuff.Profiler.ResumeTiming();

			// Most GIF viewers clear the background to transparent instead of the background color when the next frame has transparency
			bool fullScreen = src.Width == anicnt.ScreenWidth && src.Height == anicnt.ScreenHeight;
			if (!fullScreen && LastDisposal == FrameDisposalMethod.RestoreBackground)
				MemoryMarshal.Cast<byte, uint>(bspan).Fill(anifrm.HasAlpha ? 0 : (uint)anicnt.BackgroundColor);

			// Similarly, they overwrite a background color with transparent pixels but overlay instead when the previous frame is preserved
			var fspan = bspan.Slice(anifrm.OffsetTop * fbuff.Stride + anifrm.OffsetLeft * bytesPerPixel);
			fixed (byte* buff = fspan)
			{
				if (!anifrm.HasAlpha || LastDisposal == FrameDisposalMethod.RestoreBackground)
				{
					if (frame is WicGifFrame wicFrame)
						HRESULT.Check(wicFrame.WicSource->CopyPixels(null, (uint)fbuff.Stride, (uint)fspan.Length, buff));
					else
						src.CopyPixels(new Rectangle(0, 0, src.Width, src.Height), fbuff.Stride, fspan);
				}
				else
				{
					var area = new PixelArea(anifrm.OffsetLeft, anifrm.OffsetTop, src.Width, src.Height);
					using var frameSource = frame is WicGifFrame wicFrame ? wicFrame.Source : src.AsPixelSource();
					using var overlay = new OverlayTransform(fbuff, frameSource, anifrm.OffsetLeft, anifrm.OffsetTop, true, true);
					overlay.CopyPixels(area, fbuff.Stride, fspan.Length, (IntPtr)buff);
				}
			}

			fbuff.Profiler.PauseTiming();
		}

		public void Dispose()
		{
			FrameBufferSource?.DisposeBuffer();
		}
	}
}
