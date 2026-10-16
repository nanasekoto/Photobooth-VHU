# Photobooth VHU v0.2.0

Ứng dụng photobooth Windows WPF **.NET 8 x64**, giao diện xanh dương–trắng. Thanh tiêu đề hiển thị `v0.2.0-net8-review` để phân biệt với bản Mirai cũ.

## Luồng mới

1. Chọn riêng nguồn **Live View** (webcam/HDMI capture card thật hoặc hình giả lập) và nguồn **chụp ảnh** (giả lập 6000×4000, Canon USB tether hoặc frame video).
2. Bấm **BẮT ĐẦU CHỤP (4 TẤM)**. Preview tiếp tục cập nhật khi đếm 3–2–1; AF ở 1, kích chụp khi về 0, chờ tải ảnh rồi sang tấm tiếp.
3. Ngay sau ảnh thứ 4, mở popup **Xem lại & Trang trí**. Popup tự chuẩn bị ảnh, hiển thị bốn ảnh lẻ và ảnh ghép. Có thể đổi **Lưới 2×2 ↔ Dải dọc — 1 cột × 4 hàng**, tự thay frame và thêm/kéo/resize/xóa sticker mà không chụp lại.
4. Chỉ bấm **Hoàn tất & Nhận mã QR** trong popup mới xuất 5 JPG và mở cửa sổ Loading → QR.
5. Lỗi Drive: bấm **Sửa Drive / Đăng nhập**, lưu cấu hình và **Thử lại** ngay trong phiên. Ảnh, bố cục và sticker được giữ nguyên.

## Chạy trên máy có SDK 8.0.423

`Photobooth.App.csproj` đích `net8.0-windows`, `global.json` chọn SDK 8.0.423 hoặc feature band .NET 8 mới hơn. Gói DPAPI cũng chuyển về phiên bản 8.0.0. Mở terminal ở thư mục repository rồi chạy khi bạn muốn thử:

```powershell
dotnet run --project Photobooth.App/Photobooth.App.csproj -c Release
```

Chọn đúng nhánh `codex/vhu-photobooth-update` của [PR #1](https://github.com/nanasekoto/Photobooth-VHU/pull/1) cho tới khi PR được merge. Không chạy EXE/DLL trong thư mục build cũ từ lần tải trước. Các thư mục `bin/obj` đã bỏ theo dõi trong Git; không có EXE mới được phát hành trong thay đổi này.

Để thử chuyển động người thật **không cần Canon**: Cài đặt → Live View thật → chọn webcam/capture card → Chụp giả lập 6000×4000 → Lưu → Kết nối. Ảnh giả lập lấy hình từ preview, được phóng/center-crop về 6000×4000 và đóng nhãn **không phải ảnh Canon**. Phóng lớn ảnh video không tạo thêm chi tiết thật.

## Tỷ lệ và file kết quả

- Chung một hàm center-crop 3:2 cho preview và ảnh đơn: không ép ảnh webcam 16:9 vào ô dọc gây méo.
- Bốn ảnh lẻ: `photo_01.jpg`–`photo_04.jpg`, **4200×2800**, có màu và frame đơn.
- Ảnh ghép: `photobooth_vhu.jpg`, **8400×5600** (lưới) hoặc **4200×11200** (dải dọc), có frame bố cục và sticker. Mỗi ô vẫn là 4200×2800.
- Ba PNG frame riêng, kiểm tra tỷ lệ 3:2 / 3:2 / 3:8 khi chọn và khi sử dụng; lệch quá 1% sẽ cảnh báo và từ chối.
- Vị trí sticker X/Y chuẩn hóa 0..1 được giữ khi đổi layout; giữ tỷ lệ PNG. Sticker gần cạnh có thể tạm thu nhỏ để không tràn ảnh.
- JPEG Quality mặc định 90, độc lập với kích thước mỗi chiều 70% của JPEG Canon 6000×4000.

Xem **[SETUP-VHU.md](SETUP-VHU.md)** cho Canon SDK, OAuth, Drive và cách nhận biết phiên bản. Chi tiết sáu nhóm sửa ở **[CHANGELOG.md](CHANGELOG.md)**.

## Trạng thái xác nhận

Theo yêu cầu chủ dự án, lần sửa này **không chạy ứng dụng, restore/build hoặc kiểm thử**. Các hành vi trên mô tả mã đã triển khai, không phải kết quả nghiệm thu. Canon cần DLL EDSDK x64 chính thức; Drive cần OAuth và tài khoản có quyền chia sẻ. Các tài liệu thiết kế cũ được giữ làm lịch sử; README, SETUP-VHU và CHANGELOG mô tả phiên bản hiện tại.
