# Thay đổi

## 0.2.0-net8-review — 16/10/2026

Sửa theo sáu nhóm yêu cầu được chủ dự án cung cấp trong cuộc trò chuyện:

1. **.NET 8:** target `net8.0-windows`, SDK 8.0.423 trong global.json, DPAPI 8.0.0, version mới trong thanh tiêu đề. Bỏ theo dõi toàn bộ bin/obj cũ bằng `git rm --cached`; giữ ignore.
2. **Live view khi chụp:** nguồn preview và capture chọn độc lập; webcam/capture card thật có thể đi cùng chụp giả lập 6000×4000. Preview có một worker STA sống lâu và chỉ một job đang chạy, không tạo thread mỗi frame.
3. **Popup sau ảnh thứ 4:** mở ReviewWindow trước xử lý ảnh độ phân giải cao; popup có trạng thái chuẩn bị và tự hiện ảnh khi dựng xong.
4. **Biên tập trong popup:** 4 thumbnail có frame đơn, preview ghép, đổi Grid2x2/VerticalStrip và frame tương ứng, thư viện PNG, kéo/resize/xóa sticker. Giữ tọa độ X/Y chuẩn hóa khi chuyển layout, giữ tỷ lệ sticker và fit trong canvas. Tay nắm có kích thước màn hình ổn định.
5. **Tỷ lệ:** xóa ComposeStrip cũ, dùng center-crop 3:2 chung cho Canon/webcam/preview, chuẩn hóa 4200×2800 mỗi ô. Xác nhận tỷ lệ ba frame khi chọn và khi render.
6. **Xác nhận rồi xuất:** SessionDelivery chỉ xuất sau nút hoàn tất, giữ revision biên tập và thư mục phiên qua retry. Sửa Drive/đăng nhập lại trong popup lỗi, lấy cấu hình mới mỗi lần thử. Đổi nơi lưu tạo lại kết quả ở nơi mới và giữ bản cũ; không mất ảnh hoặc trang trí.

**Giới hạn xác nhận:** không chạy/build/test trong lần sửa. Không phát hành binary mới; wrapper Canon và luồng Google vẫn cần cấu hình, nghiệm thu thực tế. PR giữ dạng draft cho tới khi được nghiệm thu.
