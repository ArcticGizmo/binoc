#!/usr/bin/env pwsh
# Regenerates every icon asset from the source-of-truth SVG (binoc.svg).
#
#   src/Binoc.App/Assets/binoc.png   256x256 PNG    (window icon + in-app logo)
#   src/Binoc.App/Assets/binoc.ico   multi-res ICO  (window + .exe ApplicationIcon)
#   landing-icon.png                  512x512 PNG    (README header)
#
# The rasters are produced by tools/IconGen (renders the SVG via Svg.Skia / SkiaSharp).
#
# Run this after editing binoc.svg, then commit the regenerated assets.

$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'IconGen'
dotnet run --project $proj -c Release
