{
  description = "A Nix flake for the Kinetix Mod Manager";
  # build with: nix build .#default

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};

        # Read version from Directory.Build.props so it can't drift.
        # Assumes a literal <Version>1.6.1</Version> tag.
        versionFileContents = builtins.readFile ./Directory.Build.props;
        versionMatchLine = line:
            builtins.match ".*<Version>([^<]+)</Version>.*" line;
        versionLine = pkgs.lib.findFirst
            (line: versionMatchLine line != null)
            null
            (pkgs.lib.splitString "\n" versionFileContents);
        version =
            if versionLine == null then
            "unstable-${self.shortRev or "dirty"}"
            else
            pkgs.lib.strings.trim (builtins.head (versionMatchLine versionLine));

        # Native libraries shared between build time and runtime.
        # (GTK/GStreamer/etc. need both: headers + pkg-config at build,
        #  .so files on LD_LIBRARY_PATH at runtime.)
        nativeLibs = with pkgs; [
          gtk4
          libadwaita
          glib
          gdk-pixbuf
          speechd
          gst_all_1.gstreamer
          gst_all_1.gst-plugins-base
          gst_all_1.gst-plugins-good
          libsecret
        ];
      in
      {
        packages.default = pkgs.buildDotnetModule {
          pname = "kinetix-mod-manager";
          version = version;
          src = ./.;

          # Only expose the GTK binary. The Windows one compiles but cannot run on Linux.
          executables = [ "Kinetix.Gtk" ];

          # Build the entire solution.
          # This file is at the repository root (KinetixModManager.slnx).
          # Omitting projectFile also makes buildDotnetModule auto-detect a .sln,
          # but being explicit is safer for a .slnx.
          projectFile = "KinetixModManager.slnx";

          # The projects target .NET 10.
          dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;

          # Generated via the fetch-deps script.
          # To run script use: nix build .#default.passthru.fetch-deps && ./result "$PWD/deps.json"
          nugetDeps = ./deps.json;

          # This is the critical flag that lets the Windows-targeting projects
          # (KinetixModManager, WinForms) compile on Linux.
          dotnetFlags = [ "-p:EnableWindowsTargeting=true" ];

          # .NET's build system has a race condition when building this
          # multi-targeted solution in parallel, causing file lock errors.
          # Disabling parallel building forces a sequential, reliable build.
          enableParallelBuilding = false;

          buildInputs = nativeLibs;
          runtimeDeps = nativeLibs;

          nativeBuildInputs = with pkgs; [
            pkg-config
            wrapGAppsHook4
          ];

          meta = with pkgs.lib; {
            description = "A screen-reader-first mod manager for PC games";
            homepage = "https://github.com/SeanTerry01/Kinetix-Mod-Manager";
            license = licenses.gpl3Plus;
            maintainers = with maintainers; [ ];
          };
        };
      });
}