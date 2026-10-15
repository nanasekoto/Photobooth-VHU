# QR code and cloud sharing implementation plan

## Recommended production flow

1. Export the four individual JPGs and the composed strip to the local session folder first. Local export should succeed even when the internet is down.
2. Upload the final strip (and optionally the four individual photos) directly through Google Drive API to a dedicated event/session folder. This avoids relying on an uncertain desktop-sync delay for the instant QR step.
3. Create a Drive `reader` permission with `type=anyone` for each item that should be downloadable by guests, and retrieve the resulting `webViewLink`/share URL.
4. Generate a QR code from the share URL and display it on the result screen. The guest can scan it with a phone camera.
5. Show distinct states: `Đang tải lên`, `Đã sẵn sàng`, `Chưa có Internet` and `Lỗi chia sẻ`. Never display a QR as successful before a real share URL is returned.
6. Add a cleanup policy for event data and revoke/delete public links or files when the configured retention period ends.

## Privacy and reliability

- `Anyone with the link` means that anyone who obtains the URL may access the shared item. Treat event photos as public to link holders and tell the event operator this clearly.
- Keep OAuth refresh tokens out of source control and store credentials using an OS-appropriate secure store.
- Do not expose service-account secrets in a desktop client. If using a service account, keep it on a backend controlled by the operator.
- Store the final image locally before upload so temporary network failure does not lose the customer's session.
- A local network-only URL is an alternative for private, same-Wi-Fi downloads, but it will not work for phones outside that network without a public route/tunnel.

## Google Drive for Desktop alternative

The application can save directly into a folder mirrored by Drive for desktop for a simple backup workflow. That sync alone does not give the application a share URL instantly; the app still needs Drive API integration (or a reliable subsequent lookup) to create sharing permissions and construct the QR target.
