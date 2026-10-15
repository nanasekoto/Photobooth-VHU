# Photobooth VHU

Ứng dụng photobooth Windows C# / WPF / .NET 10 x64 cho hoạt động sinh viên Đại học Văn Hiến, giao diện xanh dương–trắng.

## Luồng sử dụng

1. Kết nối live view và bộ điều khiển chụp; chọn **Lưới 2×2** hoặc **Dải dọc 1×4**, chọn màu/LUT.
2. Bấm **BẮT ĐẦU CHỤP (4 TẤM)**: đếm 3–2–1, AF ở 1, chụp tại 0, chờ JPEG xong rồi sang ảnh tiếp.
3. Sau 4 ảnh, trang trí sticker trên ảnh ghép: thêm, kéo thả, kéo góc đổi kích thước hoặc xóa.
4. Bấm **Hoàn tất & Nhận mã QR**: popup Loading, xuất 5 file vào thư mục phiên trong Drive for desktop, chờ đồng bộ và lấy URL chia sẻ thư mục bằng Drive API v3, đổi popup sang QR.

## Những phần trong mã nguồn cập nhật

- Sidebar thu gọn/mở rộng, lưu cài đặt, ba frame PNG độc lập (ảnh đơn, lưới, dải dọc), ba khung VHU tạm và sticker mặc định được tạo lần đầu.
- JPEG nguồn 6000×4000; ảnh lẻ 4200×2800. Grid2x2: 8400×5600; VerticalStrip: 4200×11200. Kích thước mỗi chiều bằng 70%; JPEG Quality là thông số riêng.
- Đúng 5 file kết quả: `photo_01.jpg`–`photo_04.jpg` (màu + frame đơn), `photobooth_vhu.jpg` (màu + frame bố cục + sticker).
- `ILiveViewSource` cho HDMI/UVC (OpenCV, chọn thiết bị); `IPhotoCaptureController` cho Canon EDSDK hoặc giả lập 6000×4000 có nhãn.
- OAuth Desktop PKCE, refresh token mã hóa bằng Windows DPAPI, chờ Drive khớp đủ tên/dung lượng/MD5, QR từ `webViewLink` thật; có timeout và thử lại.

## Thiết lập

Đọc **[SETUP-VHU.md](SETUP-VHU.md)** để cấu hình Canon SDK x64, camera, capture card, Google Drive for desktop, OAuth và frame.

Mặc định dùng **Giả lập**; không coi ảnh minh họa là ảnh Canon. Canon cần DLL chính thức do người vận hành cung cấp. Drive cần OAuth Desktop App và Folder ID của đúng thư mục xuất.

## Trạng thái xác nhận

**Chưa chạy ứng dụng, chưa restore/build, chưa chạy kiểm thử hoặc thử phần cứng trong lần cập nhật này**, theo yêu cầu chủ dự án. Code đã viết không đồng nghĩa đã nghiệm thu Canon, tài khoản Drive hoặc tốc độ đồng bộ. Cần thực hiện các bước nghiệm thu trong hướng dẫn trước khi dùng tại sự kiện.

Các file `CAMERA-INTEGRATION.md`, `CANON-R8-INTEGRATION.md`, `QR-SHARING-DESIGN.md` được giữ làm tài liệu lịch sử. Hành vi bản cập nhật được mô tả trong README và SETUP-VHU; yêu cầu HDMI + USB tether và Drive for desktop có ưu tiên hơn các phương án cũ.