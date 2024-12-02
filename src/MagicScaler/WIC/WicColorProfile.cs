// Copyright © Clinton Ingram and Contributors
// SPDX-License-Identifier: MIT

using System;

using TerraFX.Interop.Windows;

using PhotoSauce.Interop.Wic;

namespace PhotoSauce.MagicScaler;

internal sealed unsafe class WicColorProfile(IWICColorContext* cc, ColorProfile? prof, bool ownctx = false) : IDisposable
{
	public static readonly Lazy<WicColorProfile> Cmyk = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.CmykDefault.Value), null));
	public static readonly Lazy<WicColorProfile> Srgb = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.sRgbV4.Value), ColorProfile.sRGB));
	public static readonly Lazy<WicColorProfile> Grey = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.sGreyV4.Value), ColorProfile.sGrey));
	public static readonly Lazy<WicColorProfile> AdobeRgb = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.AdobeRgbV4.Value), ColorProfile.AdobeRgb));
	public static readonly Lazy<WicColorProfile> DisplayP3 = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.DisplayP3V4.Value), ColorProfile.DisplayP3));
	public static readonly Lazy<WicColorProfile> SrgbCompact = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.sRgbCompact.Value), ColorProfile.sRGB));
	public static readonly Lazy<WicColorProfile> GreyCompact = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.sGreyCompact.Value), ColorProfile.sGrey));
	public static readonly Lazy<WicColorProfile> AdobeRgbCompact = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.AdobeRgbCompact.Value), ColorProfile.AdobeRgb));
	public static readonly Lazy<WicColorProfile> DisplayP3Compact = new(() => new WicColorProfile(CreateContextFromProfile(IccProfiles.DisplayP3Compact.Value), ColorProfile.DisplayP3));

	private readonly bool ownContext = ownctx;

	public IWICColorContext* WicColorContext { get; private set; } = cc;
	public ColorProfile ParsedProfile { get; } = prof ?? GetProfileFromContext(cc, 0);

	public static WicColorProfile GetDefaultFor(PixelFormat fmt) => fmt.ColorRepresentation switch {
		PixelColorRepresentation.Cmyk => Cmyk.Value,
		PixelColorRepresentation.Grey => Grey.Value,
		_                             => Srgb.Value
	};

	public static WicColorProfile GetSourceProfile(WicColorProfile wicprof, ColorProfileMode mode)
	{
		var prof = ColorProfile.GetSourceProfile(wicprof.ParsedProfile, mode);
		return MapKnownProfile(prof) ?? wicprof;
	}

	public static WicColorProfile GetDestProfile(WicColorProfile wicprof, ColorProfileMode mode)
	{
		var prof = ColorProfile.GetDestProfile(wicprof.ParsedProfile, mode);
		return MapKnownProfile(prof) ?? wicprof;
	}

	public static WicColorProfile? MapKnownProfile(ColorProfile prof)
	{
		if (prof == ColorProfile.sGrey)
			return Grey.Value;
		if (prof == ColorProfile.sRGB)
			return Srgb.Value;
		if (prof == ColorProfile.AdobeRgb)
			return AdobeRgb.Value;
		if (prof == ColorProfile.DisplayP3)
			return DisplayP3.Value;

		return null;
	}

	public static IWICColorContext* CreateContextFromProfile(Span<byte> profile)
	{
		fixed (byte* pprof = profile)
		{
			using var cc = default(ComPtr<IWICColorContext>);
			HRESULT.Check(Wic.Factory->CreateColorContext(cc.GetAddressOf()));
			HRESULT.Check(cc.Get()->InitializeFromMemory(pprof, (uint)profile.Length));
			return cc.Detach();
		}
	}

	public static ColorProfile GetProfileFromContext(IWICColorContext* cc, uint cb)
	{
		if (cb == 0u)
			HRESULT.Check(cc->GetProfileBytes(0, null, &cb));

		using var buff = BufferPool.RentLocal<byte>((int)cb);
		fixed (byte* pbuff = buff)
			HRESULT.Check(cc->GetProfileBytes(cb, pbuff, &cb));

		return ColorProfile.Cache.GetOrAdd(buff.Span);
	}

	private void dispose(bool disposing)
	{
		if (disposing)
			GC.SuppressFinalize(this);

		if (!ownContext || WicColorContext is null)
			return;

		WicColorContext->Release();
		WicColorContext = null;
	}

	public void Dispose() => dispose(true);

	~WicColorProfile()
	{
		ThrowHelper.ThrowIfFinalizerExceptionsEnabled(nameof(WicColorProfile));

		dispose(false);
	}
}
