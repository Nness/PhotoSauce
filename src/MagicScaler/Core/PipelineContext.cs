// Copyright © Clinton Ingram and Contributors.  Licensed under the MIT License.

using System;
using System.Linq;
using System.Collections.Generic;

namespace PhotoSauce.MagicScaler
{
	internal sealed class PipelineContext : IDisposable
	{
		private readonly bool ownContainer;

		private List<IProfiler>? profilers;
		private Stack<IDisposable>? disposables;
		private WicPipelineContext? wicContext;

		public ProcessImageSettings Settings { get; }
		public ProcessImageSettings UsedSettings { get; private set; }
		public Orientation Orientation { get; set; }

		public IImageContainer ImageContainer { get; set; }
		public IImageFrame ImageFrame { get; set; }
		public PixelSource Source { get; set; } = NoopPixelSource.Instance;
		public IMetadataSource Metadata { get; set; } = NoopMetadataSource.Instance;

		public AnimationPipelineContext? AnimationContext { get; set; }

		public ColorProfile? SourceColorProfile { get; set; }
		public ColorProfile? DestColorProfile { get; set; }

		public IEnumerable<PixelSourceStats> Stats => profilers?.OfType<ProcessingProfiler>().Select(p => p.Stats!) ?? Enumerable.Empty<PixelSourceStats>();

		public WicPipelineContext WicContext => wicContext ??= new WicPipelineContext();

		public bool IsAnimatedGifPipeline =>
			ImageContainer.IsAnimation && Settings.SaveFormat is FileFormat.Gif or FileFormat.Auto && ImageContainer.FrameCount > 1;

		public PipelineContext(ProcessImageSettings settings, IImageContainer cont, bool ownCont = true)
		{
			Settings = settings.Clone();
			ImageContainer = cont;
			ownContainer = ownCont;

			ImageFrame = null!;
			UsedSettings = null!;
		}

		public T AddDispose<T>(T disposeHandle) where T : IDisposable
		{
			(disposables ??= new()).Push(disposeHandle);

			return disposeHandle;
		}

		public IProfiler AddProfiler(string name)
		{
			if (!StatsManager.ProfilingEnabled)
				return NoopProfiler.Instance;

			var prof = new ProcessingProfiler(name);
			(profilers ??= new(capacity: 8)).Add(prof);

			return prof;
		}

		public T AddProfiler<T>(T source) where T : IProfileSource
		{
			if (StatsManager.ProfilingEnabled)
			{
				profilers ??= new(capacity: 8);
				if (!profilers.Contains(source.Profiler))
					profilers.Add(source.Profiler);
			}

			return source;
		}

		public void FinalizeSettings()
		{
			Orientation = Settings.OrientationMode == OrientationMode.Normalize ? ImageFrame.ExifOrientation : Orientation.Normal;

			if (!Settings.IsNormalized)
			{
				Settings.Fixup(Source.Width, Source.Height, Orientation.SwapsDimensions());

				if (Settings.SaveFormat == FileFormat.Auto)
					Settings.SetSaveFormat(ImageContainer.ContainerFormat, Source.Format.AlphaRepresentation != PixelAlphaRepresentation.None);

				if (Settings.ColorProfileMode <= ColorProfileMode.NormalizeAndEmbed && !Settings.EncoderInfo.SupportsColorProfile)
					Settings.ColorProfileMode = ColorProfileMode.ConvertToSrgb;
			}

			if (Settings.SaveFormat == FileFormat.Png8 && Settings.EncoderOptions is not PngIndexedEncoderOptions)
				Settings.EncoderOptions = PngIndexedEncoderOptions.Default;

			if (Settings.SaveFormat == FileFormat.Gif && Settings.EncoderOptions is not GifEncoderOptions)
				Settings.EncoderOptions = GifEncoderOptions.Default;

			if (Settings.EncoderOptions is null)
				Settings.EncoderOptions = Settings.EncoderInfo.DefaultOptions;

			UsedSettings = Settings.Clone();
		}

		public void Dispose()
		{
			wicContext?.Dispose();
			Source.Dispose();
			AnimationContext?.Dispose();
			ImageFrame?.Dispose();

			if (ownContainer)
				ImageContainer?.Dispose();

			while (disposables?.Count > 0)
				disposables.Pop().Dispose();
		}
	}
}
