# Camera integration decision notes

## Keep capture and live view separate

- `ICameraController` handles connection, capture and image transfer.
- A separate `ILiveViewSource` should provide frames from the vendor SDK or an HDMI/UVC capture card.
- HDMI capture supplies a video feed; it does not by itself guarantee camera autofocus, shutter release or transfer of the full-resolution still image.

## Before implementing a vendor adapter

Record the exact camera model, body firmware, Windows version, USB mode, whether the camera must save to both SD card and PC, and whether RAW or camera JPEG is the processing source. Verify that the manufacturer's currently published SDK supports that model and the required capture/live-view functions.

## Suggested adapter boundary

A production adapter should expose:

- connection state and model name;
- start/stop live view;
- autofocus or focus command when supported;
- capture still image;
- receive the complete still image and confirm its transfer completed;
- cancellation, timeout and reconnection handling;
- camera busy/card-full/USB-disconnected errors.

Do not implement a universal driver by assuming every camera vendor exposes the same USB protocol. Keep Canon/Sony/other adapters in separate classes and only enable capabilities verified for the attached model.
