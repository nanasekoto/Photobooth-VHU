# Thiết lập Photobooth VHU v0.2.0 — .NET 8

## 1. Nguồn đúng và phiên bản đúng

- Nguồn cập nhật: nhánh `codex/vhu-photobooth-update`, PR #1. `main` chỉ thay đổi sau khi PR được merge.
- Project chạy: `Photobooth.App/Photobooth.App.csproj`, đích **net8.0-windows**, x64.
- `global.json` chọn **8.0.423** với roll-forward trong .NET 8. Không yêu cầu .NET 10 để build project này.
- Thanh tiêu đề mới hiển thị **Photobooth VHU v0.2.0-net8-review • .NET 8 • x64** (có thể kèm metadata commit nếu môi trường build thêm vào version).
- Các file build cũ trong `bin/obj` đã được bỏ theo dõi bằng `git rm --cached`; `.gitignore` giữ chúng khỏi commit mới. Thao tác này không xóa file cũ trên máy. Đừng chạy EXE `net10.0-windows` còn sót từ lần tải trước.

Khi người vận hành muốn chạy thử, từ thư mục repository:

```powershell
dotnet run --project Photobooth.App/Photobooth.App.csproj -c Release
```

Nếu có dùng Visual Studio, chọn môi trường hỗ trợ .NET 8 và WPF desktop. Nguồn [NETSDK1045 của Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1045) giải thích lỗi SDK không hỗ trợ framework đích. Việc đổi target nhằm khắc phục nguyên nhân cấu hình này; chưa có kết quả build trong lần bàn giao.

## 2. Thử không cần Canon nhưng vẫn thấy người thật

Mở **Cài đặt**:

1. **Nguồn Live View:** `Video thật • Webcam / HDMI Capture Card`.
2. Chọn đúng tên/chỉ số thiết bị DirectShow.
3. **Nguồn ảnh chụp:** `Giả lập 6000×4000 từ preview`.
4. Lưu rồi bấm **Kết nối**.

Preview đọc video liên tục trên vòng lặp riêng; bộ lọc/center-crop chạy trên một worker STA sống lâu, chỉ một công việc preview đang xử lý để không tồn hàng đợi. Trạng thái Capturing không chặn cập nhật preview.

Chế độ chụp giả lập dùng frame preview tại lượt chụp, center-crop/phóng về 6000×4000 rồi đóng nhãn giả lập. Đây là ảnh video được phóng lớn, **không phải JPEG gốc Canon**, không khẳng định chất lượng chi tiết 24 MP.

Có thêm chế độ **Ảnh từ frame webcam/capture card** để nhận frame ở kích thước video gốc, sau đó dùng chung bước chuẩn hóa 3:2 khi tạo ảnh kết quả. Nếu không có bất kỳ thiết bị video nào, chọn Live View hình giả lập tĩnh; hình đó không thể phản ánh chuyển động người thật.

## 3. Popup Xem lại & Trang trí

- Sau khi tải đủ ảnh thứ 4, popup mở ngay và tự chuẩn bị ảnh ở nền; chưa xuất 5 file kết quả ở bước này.
- Phía trên có 4 ảnh đơn với frame đơn; vùng lớn là ảnh ghép.
- Chọn lưới 2×2 hoặc **dải dọc 1 cột × 4 hàng**. Frame lưới/dải được lấy riêng từ cấu hình đã nạp, không cần chụp lại.
- Bấm sticker trong thư viện, kéo để di chuyển, kéo góc để resize, bấm × để xóa. Có thể nhập PNG thêm vào thư viện.
- Vị trí X/Y trong 0..1 giữ nguyên khi đổi bố cục. Chiều cao được tính theo tỷ lệ ảnh sticker, không kéo méo sticker để giữ cả hai kích thước chuẩn hóa. Nếu sát cạnh, kích thước hiển thị được thu nhỏ vừa canvas; chuyển lại bố cục có thể khôi phục chiều rộng mong muốn. Khi trực tiếp kéo/resize, kích thước hiện tại trở thành lựa chọn mới.
- Tay nắm resize/xóa giữ kích thước hiển thị khoảng 24 pixel khi popup thay đổi kích thước hoặc xem dải dọc, không trở thành chấm quá nhỏ theo tỷ lệ canvas.
- Chỉ bấm **Hoàn tất & Nhận mã QR** mới xuất file và mở Loading/QR. Đóng popup trước khi xác nhận sẽ không xuất kết quả; JPEG nguồn vẫn được giữ trong Working.

## 4. Frame, center-crop và kết xuất

Các PNG mặc định được tạo lần đầu trong `%LOCALAPPDATA%\PhotoboothVHU\Frames`; thư viện sticker ở `Stickers`. Frame VHU tạm không phải thiết kế hoặc logo chính thức đã được trường phê duyệt.

| Frame | Tỷ lệ PNG cần nạp | Ảnh áp dụng |
| --- | --- | --- |
| Ảnh đơn | 3:2 ngang | Từng ảnh lẻ, dùng overlay live view |
| Lưới | 3:2 ngang | Ảnh ghép 2×2 |
| Dải dọc | 3:8 | Ảnh ghép 1 cột × 4 hàng |

Tỷ lệ PNG lệch quá 1% bị báo lỗi khi chọn/lưu và được xác nhận lại khi dựng ảnh. Nền PNG cần trong suốt ở các vùng để ảnh hiện bên dưới.

`BitmapTools.NormalizeLandscape` lấy vùng giữa có tỷ lệ đúng 3:2 trước khi resize; dùng cho cả preview, ảnh webcam/capture card 16:9 và ảnh Canon. Việc crop có thể cắt bớt hai mép ảnh, nhưng không kéo giãn khuôn mặt. Hàm ghép cũ `ComposeStrip` với ô 1100×1650 đã được xóa.

Xuất đúng 5 file trong `yyyyMMdd_HHmmss`:

- `photo_01.jpg`–`photo_04.jpg`: 4200×2800, đã áp màu và frame đơn, không có sticker.
- `photobooth_vhu.jpg`: 8400×5600 hoặc 4200×11200, đã áp màu, frame bố cục và sticker.

Mỗi ô ghép ngang 3:2. Canvas không thêm gutter/footer ngoài các ô. 70% theo mỗi chiều áp cho nguồn Canon 6000×4000; nguồn video nhỏ hơn được chuẩn hóa về cùng kích thước xuất và không có thêm chi tiết thật. JPEG Quality riêng, mặc định 90.

## 5. Canon R8 thật

1. Nối micro HDMI → capture card → PC cho live view; USB tether riêng để AF, chụp và nhận JPEG.
2. Chọn Live View thật và **Canon R8 • JPEG qua USB tether** trong Cài đặt.
3. Tải EDSDK Windows x64 chính thức từ Canon. Đặt DLL cần thiết của **cùng bộ SDK** cạnh EXE mới `net8.0-windows`, không cạnh EXE cũ. Không đưa DLL SDK lên GitHub.
4. Đóng EOS Utility. Máy ảnh dùng `Photo Import/Remote Control`, chế độ chụp JPEG Large 6000×4000; không dùng RAW-only. Adapter đặt SaveTo Host; không cam kết đồng thời lưu thẻ SD.
5. Luồng AF ở số 1, nhấn hết cò khi về 0; chờ sự kiện truyền JPEG xong mới bắt đầu ảnh tiếp. Nếu AF chưa xong hoặc máy bận, không có cam kết màn trập vật lý đúng một mốc thời gian tuyệt đối.

Wrapper native vẫn cần đối chiếu header của bộ EDSDK được cài và nghiệm thu trên R8 thật. Nguồn: [Canon SDK](https://asia.canon/en/campaign/developerresources/sdk), [hướng dẫn USB R8](https://cam.start.canon/en/C013/manual/html/UG-08_Set-up_0270.html).

## 6. Google Drive và xử lý lỗi ngay trong phiên

Chuẩn bị Drive for desktop đã đăng nhập, thư mục xuất cục bộ, **Folder ID của đúng thư mục cha trên Drive**, JSON OAuth loại **Desktop App**, Drive API đã bật. Dùng cùng tài khoản có quyền đọc và chia sẻ thư mục. Nhấn Đăng nhập Google để cấp quyền qua trình duyệt.

Ứng dụng dùng scope Drive để tìm file do Drive for desktop tạo, không upload trùng qua API. Refresh token được mã hóa Windows DPAPI cho người dùng hiện tại; không đưa token/OAuth JSON vào repository. Thư mục phiên được chia sẻ reader cho bất kỳ ai có liên kết; chính sách tài khoản có thể chặn việc này.

Sau xác nhận:

1. Mở Loading và xuất 5 JPG.
2. Drive for desktop đồng bộ; API tìm đúng thư mục trong parent đã cấu hình và chờ đủ 5 file khớp tên/dung lượng/MD5.
3. Lấy URL chia sẻ thư mục thật, hiện QR.

Nếu lỗi, trong chính cửa sổ đó có **Sửa Drive / Đăng nhập**:

- Sửa parent Folder ID, OAuth JSON, thời gian chờ hoặc đăng nhập lại mà không mất bốn ảnh/bố cục/sticker.
- Có thể đổi đường dẫn lưu. Lần Thử lại xuất lại 5 file vào thư mục cùng phiên ở vị trí mới; giữ bản cũ. Nếu thư mục đích của phiên khác đã tồn tại, ứng dụng từ chối ghi đè và yêu cầu chọn thư mục cha khác.
- Bấm **Lưu và trở lại phiên hiện tại** rồi **Thử lại**. Dịch vụ lấy cấu hình Drive mới ở mỗi lần thử, không dùng snapshot cũ.
- Nếu chỉ đổi thông tin chia sẻ, không xuất lại 5 file đã hoàn tất. Nếu thay bố cục/sticker rồi xác nhận lại, dịch vụ xuất lại đúng 5 file cùng phiên trước khi chờ Drive.
- Đóng Loading/QR hủy chờ và trả về popup xem lại; phần trang trí vẫn còn trong phiên đang mở. Đóng toàn bộ ứng dụng chưa có cơ chế tự khôi phục hàng đợi QR.

## 7. Dữ liệu và trạng thái bàn giao

`%LOCALAPPDATA%\PhotoboothVHU` chứa settings, token mã hóa, frame/sticker và JPEG nguồn trong `Working/<id>`. Chưa tự dọn Working; người vận hành cần dọn sau khi giữ kết quả cần thiết. Thư mục Drive của khách chỉ có 5 file kết quả sau khi xuất thành công.

Lần sửa này **không chạy ứng dụng, restore/build hoặc test**, theo yêu cầu đang áp dụng. Không có EXE mới, không có xác nhận FPS/thời gian xử lý, hoạt động Canon hoặc quyền chia sẻ Google. Đọc CHANGELOG cho danh sách thay đổi; chỉ xem là đã nghiệm thu sau khi chủ dự án cho chạy thử trên môi trường thật.
