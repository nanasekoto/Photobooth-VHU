# Mirai Photobooth — starter project

A Windows WPF/.NET starter for a touchscreen photobooth workflow. This is intentionally a **demo/MVP shell**, not a production camera-control application yet.

## Included

- Touch-friendly dark UI shell.
- Simulated camera for testing the workflow without hardware.
- 3-second countdown for each of four simulated captures.
- Basic Original, Warm, Retro, B&W, and Pastel filters.
- 3D `.cube` LUT file parsing and trilinear interpolation.
- Export of four individual JPGs and one composed photo strip.
- Selectable output folder (can be a Google Drive for desktop mirrored folder).
- `ICameraController` interface to isolate vendor-specific camera integration.

## Not included yet

- Real camera USB tether, autofocus or shutter control.
- Real HDMI/UVC live feed.
- GPU-accelerated 30 fps LUT preview.
- Draggable/rotatable sticker editor and sticker compositing.
- HALD CLUT importer.
- Google Drive API link creation, QR display, and phone download page.

All sample captures are labelled demo illustrations and are not photographs from a real camera.

## Requirements

- Windows 11 recommended.
- Visual Studio 2026 or another IDE with the .NET 10 SDK and WPF desktop workload.

## Run

1. Extract the ZIP.
2. Open `PhotoboothStarter.sln` in Visual Studio.
3. Restore/build the solution.
4. Set `Photobooth.App` as the startup project and run.
5. Click **Kết nối demo**, choose a preset, then click **BẮT ĐẦU CHỤP**.

Output defaults to `Pictures\MiraiPhotobooth\<session timestamp>\`.

## Important notes on colour

The `.cube` reader supports 3D LUT files with `LUT_3D_SIZE` and optional `DOMAIN_MIN`/`DOMAIN_MAX`; it does not currently support 1D LUTs, HALD CLUT, or every vendor-specific extension. The current CPU implementation is suitable for prototyping and high-resolution export tests, but should be replaced with a GPU shader for low-latency live preview.

Applying a LUT to camera JPEGs is the simplest first target. For RAW input, add a RAW development stage before the LUT so the source has a defined colour space and tone curve.

`Examples/identity_2.cube` is a tiny identity LUT you can load to test the LUT file path. Since it is an identity LUT, it should not visibly change the image.

## Canon EOS R8 integration

See [`CANON-R8-INTEGRATION.md`](CANON-R8-INTEGRATION.md) for confirmed SDK support, camera USB configuration, and the integration plan. The app still uses the demo camera until a native SDK adapter is implemented.
