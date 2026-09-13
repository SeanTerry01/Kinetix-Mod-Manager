using System;
using System.Runtime.InteropServices;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// A web page inside the window, through WebKitGTK — the Linux counterpart to WebView2.
///
/// <para>
/// Hand-written P/Invoke because there is no .NET binding for WebKitGTK: GirCore does not ship one, and
/// nothing else maintained does either. The surface needed is small, though, which is why this is viable at
/// all — the manager browses, reads and occasionally runs a snippet of script, and that is the whole of it.
/// </para>
///
/// <para>
/// The API version matters more than it looks. <c>webkitgtk-6.0</c> is the GTK4 build;
/// <c>webkit2gtk-4.1</c> is the GTK3 one and will not embed in a GTK4 window however willing it seems. The
/// soname below pins the right one.
/// </para>
///
/// <para>
/// Accessibility is the reason this exists rather than a browser being launched: WebKitGTK exposes the page
/// to AT-SPI, so Orca reads it with its own heading and link navigation, inside the app, with the manager's
/// own keys still working around it. A page opened in an external browser takes the user out of the program
/// and loses that continuity — which for a keyboard-and-speech user is not a small thing.
/// </para>
/// </summary>
public sealed class WebKitView
{
	private const string Lib = "libwebkitgtk-6.0.so.4";

	[DllImport(Lib, EntryPoint = "webkit_web_view_new")]
	private static extern IntPtr WebViewNew();

	[DllImport(Lib, EntryPoint = "webkit_web_view_load_uri", CharSet = CharSet.Ansi)]
	private static extern void LoadUri(IntPtr webView, string uri);

	[DllImport(Lib, EntryPoint = "webkit_web_view_get_uri")]
	private static extern IntPtr GetUri(IntPtr webView);

	[DllImport(Lib, EntryPoint = "webkit_web_view_get_title")]
	private static extern IntPtr GetTitle(IntPtr webView);

	[DllImport(Lib, EntryPoint = "webkit_web_view_go_back")]
	private static extern void GoBackNative(IntPtr webView);

	[DllImport(Lib, EntryPoint = "webkit_web_view_can_go_back")]
	private static extern bool CanGoBackNative(IntPtr webView);

	[DllImport(Lib, EntryPoint = "webkit_web_view_reload")]
	private static extern void ReloadNative(IntPtr webView);

	private readonly IntPtr _native;

	/// <summary>The widget to put in a container. Wrapped from the native pointer GTK handed back.</summary>
	public Gtk.Widget Widget { get; }

	public WebKitView()
	{
		_native = WebViewNew();
		if (_native == IntPtr.Zero) throw new InvalidOperationException("WebKitGTK returned no web view.");

		// GirCore can adopt an existing GObject, which is what lets a hand-rolled P/Invoke widget sit in a
		// GirCore layout as an ordinary child.
		Widget = (Gtk.Widget)GObject.Internal.InstanceWrapper.WrapHandle<Gtk.Widget>(_native, ownedRef: false)!;
	}

	public void Load(string uri)
	{
		if (string.IsNullOrWhiteSpace(uri)) return;

		try { LoadUri(_native, uri); }
		catch (Exception ex) { DiagnosticLog.WriteException("Web", $"loading {uri}", ex); }
	}

	public string Title => Marshal.PtrToStringUTF8(GetTitle(_native)) ?? "";

	public string Uri => Marshal.PtrToStringUTF8(GetUri(_native)) ?? "";

	public bool CanGoBack => CanGoBackNative(_native);

	public void GoBack() => GoBackNative(_native);

	public void Reload() => ReloadNative(_native);
}
